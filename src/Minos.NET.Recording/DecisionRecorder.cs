using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Minos.Recording;

/// <summary>
/// Records decisions: a bounded queue with one background writer that hands records, outcomes and erasures to an
/// <see cref="IDecisionRecordSink"/>, one call at a time and in queue order. The app owns it, every recording stage
/// shares it, and the app or its container disposes it. It never disposes the sink.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A record is queued without waiting, and dropped and counted when the queue is full.</item>
/// <item>Outcomes, erasures and flushes wait for queue space instead, up to the caller's cancellation.</item>
/// <item>A sink call that throws is logged with the exception's type name only, counted and skipped; an erasure that
/// fails makes its caller's task fail with <see cref="RecordingException"/>.</item>
/// <item><see cref="DisposeAsync"/> drains the queue for up to <see cref="RecordingOptions.FlushTimeout"/>, then cancels
/// the sink's call and gives up on the rest, which it logs by count. A sink that ignores the cancellation may still be
/// running its call when <see cref="DisposeAsync"/> returns.</item>
/// </list>
/// </remarks>
public sealed class DecisionRecorder : IDecisionRecorder, IAsyncDisposable
{
    private readonly IDecisionRecordSink _sink;
    private readonly ILogger _logger;
    private readonly TimeSpan _flushTimeout;
    private readonly Channel<WorkItem> _channel;
    private readonly CancellationTokenSource _stop = new();

    // Guards the reads from the queue, the item in flight and _stopped, so that a dispose that gives up and the writer
    // never both take an item.
    private readonly Lock _readGate = new();
    private readonly Task _writer;
    private WorkItem? _inFlight;
    private bool _stopped;
    private long _dropped;
    private int _disposed;
    private Task? _disposal;

    /// <summary>Creates the recorder and starts its writer.</summary>
    /// <param name="sink">Where the records go. The recorder does not dispose it.</param>
    /// <param name="options">The queue's capacity, the flush timeout and the redactor, or <see langword="null"/> for the defaults. They are read now.</param>
    /// <param name="loggerFactory">Logs dropped records, failed sink calls, failed redactions and a flush that timed out, or <see langword="null"/> for none.</param>
    /// <param name="timeProvider">The clock for the records' times and the flush timeout, or <see langword="null"/> for <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sink"/> is <see langword="null"/>.</exception>
    public DecisionRecorder(IDecisionRecordSink sink, RecordingOptions? options = null, ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sink);
        options ??= new RecordingOptions();
        _sink = sink;
        _flushTimeout = options.FlushTimeout;
        Redact = options.Redact;
        _logger = loggerFactory?.CreateLogger(DecisionLog.Category) ?? NullLogger.Instance;
        Time = timeProvider ?? TimeProvider.System;
        _channel = Channel.CreateBounded<WorkItem>(new BoundedChannelOptions(options.Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // The writer must not carry the creator's context, such as its activity or a recording scope.
        using (ExecutionContext.SuppressFlow())
        {
            _writer = Task.Run(RunAsync);
        }
    }

    /// <summary>Gets the writer, for tests that wait for an abandoned sink call to finish.</summary>
    internal Task Writer => _writer;

    /// <summary>Gets the clock the recording stage times its records with.</summary>
    internal TimeProvider Time { get; }

    /// <summary>Gets the redactor the recording stage runs on each state, or <see langword="null"/>.</summary>
    internal Func<DecisionContent, RedactionResult>? Redact { get; }

    /// <summary>Queues a record without waiting.</summary>
    /// <param name="record">The record.</param>
    /// <returns><see langword="true"/> when it was queued; <see langword="false"/> when the queue was full or the recorder is disposed. Either drop is counted.</returns>
    internal bool TryEnqueue(DecisionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (Volatile.Read(ref _disposed) != 0)
        {
            RecordingTelemetry.Record(RecordingTelemetry.DroppedDisposed);
            return false;
        }

        if (_channel.Writer.TryWrite(WorkItem.ForRecord(record)))
        {
            RecordingTelemetry.Queued(1);
            return true;
        }

        if (Volatile.Read(ref _disposed) != 0)
        {
            RecordingTelemetry.Record(RecordingTelemetry.DroppedDisposed);
            return false;
        }

        RecordingTelemetry.Record(RecordingTelemetry.DroppedFull);
        Interlocked.Increment(ref _dropped);
        return false;
    }

    /// <inheritdoc/>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        ThrowIfDisposed();
        return EnqueueAsync(WorkItem.ForOutcome(decisionId, outcome), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        ThrowIfDisposed();
        return EnqueueAndWaitAsync(WorkItem.ForErasure(erasureKey, null), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ThrowIfDisposed();
        return EnqueueAndWaitAsync(WorkItem.ForErasure(null, decisionId), cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask FlushAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return EnqueueAndWaitAsync(WorkItem.ForFlush(), cancellationToken);
    }

    /// <summary>
    /// Stops taking items and drains the queue for up to <see cref="RecordingOptions.FlushTimeout"/>. After that, the
    /// sink's call is cancelled, and the items not done are logged by kind in event 1012; their erasures and flushes fail
    /// with <see cref="RecordingException"/>. The sink is not disposed. A second call waits for the first.
    /// </summary>
    /// <returns>A task that completes when the queue drained or the timeout passed.</returns>
    public ValueTask DisposeAsync()
    {
        var created = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var existing = Interlocked.CompareExchange(ref _disposal, created.Task, null);
        if (existing is not null)
        {
            return new ValueTask(existing);
        }

        return DisposeCoreAsync(created);
    }

    private async ValueTask DisposeCoreAsync(TaskCompletionSource done)
    {
        Volatile.Write(ref _disposed, 1);
        _channel.Writer.TryComplete();
        try
        {
            await _writer.WaitAsync(_flushTimeout, Time).ConfigureAwait(false);

            // Only once the writer is done: after a timeout, a sink's call may still hold the token.
            _stop.Dispose();
        }
        catch (Exception)
        {
            // A timeout, or a writer that failed in a way it should not: either way, what is left is lost and counted.
            GiveUp();
        }
        finally
        {
            try
            {
                ReportDrops();
            }
            finally
            {
                done.SetResult();
            }
        }
    }

    // Cancels the sink's call and takes what is left, so the writer takes nothing more.
    private void GiveUp()
    {
        _stop.Cancel();
        var lost = new List<WorkItem>();
        lock (_readGate)
        {
            _stopped = true;

            // The item in flight is lost only when the writer has not settled it: a sink call that finished just in
            // time was counted by the writer, and one that finishes later is not counted at all.
            if (_inFlight is { } inFlight && inFlight.TrySettle())
            {
                lost.Add(inFlight);
            }

            while (_channel.Reader.TryRead(out var item))
            {
                RecordingTelemetry.Queued(-1);
                if (item.TrySettle())
                {
                    lost.Add(item);
                }
            }
        }

        // The callers are told first, so a listener that throws while the loss is counted leaves no one waiting.
        foreach (var item in lost)
        {
            if (item.Completion is { } completion)
            {
                Fail(completion, RecordingException.Lost());
            }
        }

        int records = 0, outcomes = 0, erasures = 0, flushes = 0;
        foreach (var item in lost)
        {
            switch (item.Kind)
            {
                case WorkKind.Record:
                    records++;
                    break;
                case WorkKind.Outcome:
                    outcomes++;
                    break;
                case WorkKind.Erasure:
                    erasures++;
                    break;
                default:
                    flushes++;
                    break;
            }
        }

        Log(static (logger, counts) => DecisionLog.FlushTimedOut(logger, counts.TimeoutMs, counts.Records, counts.Outcomes, counts.Erasures, counts.Flushes),
            (TimeoutMs: _flushTimeout.TotalMilliseconds, Records: records, Outcomes: outcomes, Erasures: erasures, Flushes: flushes));
        for (var i = 0; i < records; i++)
        {
            RecordingTelemetry.Record(RecordingTelemetry.DroppedFlushTimeout);
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private async ValueTask EnqueueAsync(WorkItem item, CancellationToken cancellationToken)
    {
        try
        {
            await _channel.Writer.WriteAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException ex)
        {
            // The recorder was disposed while the call waited for queue space.
            throw new ObjectDisposedException("The decision recorder is disposed.", ex);
        }

        RecordingTelemetry.Queued(1);
    }

    private async ValueTask EnqueueAndWaitAsync(WorkItem item, CancellationToken cancellationToken)
    {
        await EnqueueAsync(item, cancellationToken).ConfigureAwait(false);
        await item.Completion!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RunAsync()
    {
        var cancellationToken = _stop.Token;
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (TryTake(out var item))
                {
                    try
                    {
                        ReportDrops();
                        await ProcessAsync(item, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (!IsStop(ex, cancellationToken))
                    {
                        // Only counting or logging can throw here, through a broken listener or logger: the caller is
                        // told, and the writer goes on with the next item.
                        if (item.Completion is { } completion)
                        {
                            Fail(completion, RecordingException.SinkFailed(ex));
                        }
                    }

                    lock (_readGate)
                    {
                        _inFlight = null;
                    }
                }

                if (Volatile.Read(ref _stopped))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The dispose gave up and counted what was left.
        }
    }

    private bool TryTake(out WorkItem item)
    {
        lock (_readGate)
        {
            if (_stopped || !_channel.Reader.TryRead(out item!))
            {
                item = null!;
                return false;
            }

            _inFlight = item;
        }

        RecordingTelemetry.Queued(-1);
        return true;
    }

    // After the sink call, an item is counted and its caller told only when the writer settles it first: when the
    // dispose gave up on it meanwhile, the dispose counted it, and the late result is dropped.
    private async ValueTask ProcessAsync(WorkItem item, CancellationToken cancellationToken)
    {
        switch (item.Kind)
        {
            case WorkKind.Record:
                try
                {
                    await _sink.WriteAsync(item.Record!, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!IsStop(ex, cancellationToken))
                {
                    if (item.TrySettle())
                    {
                        RecordingTelemetry.Record(RecordingTelemetry.SinkFailed);
                        LogSinkCallFailed("write", ex);
                    }

                    return;
                }

                if (item.TrySettle())
                {
                    RecordingTelemetry.Record(RecordingTelemetry.Written);
                }

                return;
            case WorkKind.Outcome:
                try
                {
                    await _sink.AddOutcomeAsync(item.DecisionId!, item.Outcome!, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (!IsStop(ex, cancellationToken))
                {
                    if (item.TrySettle())
                    {
                        RecordingTelemetry.Outcome(RecordingTelemetry.Failed);
                        LogSinkCallFailed("add-outcome", ex);
                    }

                    return;
                }

                if (item.TrySettle())
                {
                    RecordingTelemetry.Outcome(RecordingTelemetry.Written);
                }

                return;
            case WorkKind.Erasure:
                var byKey = item.ErasureKey is not null;
                try
                {
                    if (byKey)
                    {
                        await _sink.EraseAsync(item.ErasureKey!, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        await _sink.EraseDecisionAsync(item.DecisionId!, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (!IsStop(ex, cancellationToken))
                {
                    if (item.TrySettle())
                    {
                        Fail(item.Completion!, RecordingException.SinkFailed(ex));
                        RecordingTelemetry.Erasure(RecordingTelemetry.Failed);
                        LogSinkCallFailed(byKey ? "erase" : "erase-decision", ex);
                    }

                    return;
                }

                if (item.TrySettle())
                {
                    item.Completion!.TrySetResult();
                    RecordingTelemetry.Erasure(RecordingTelemetry.Written);
                }

                return;
            default:
                if (item.TrySettle())
                {
                    item.Completion!.TrySetResult();
                }

                return;
        }
    }

    // The sink's call was cancelled because the dispose gave up: the item is the dispose's to count.
    private static bool IsStop(Exception exception, CancellationToken cancellationToken)
        => exception is OperationCanceledException && cancellationToken.IsCancellationRequested;

    private void ReportDrops()
    {
        var dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
        {
            Log(static (logger, count) => DecisionLog.RecordsDropped(logger, count), dropped);
        }
    }

    /// <summary>Logs event 1011 for a redactor that failed, with the exception's type name only.</summary>
    /// <param name="exceptionType">The type name of the redactor's exception.</param>
    internal void LogRedactionFailed(string exceptionType)
        => Log(static (logger, type) => DecisionLog.RedactionFailed(logger, type), exceptionType);

    private void LogSinkCallFailed(string operation, Exception exception)
        => Log(static (logger, call) => DecisionLog.SinkCallFailed(logger, call.Operation, call.ExceptionType), (Operation: operation, ExceptionType: exception.GetType().Name));

    // Logging is best effort: a logger that throws must not stop the writer or fail a dispose, which would leave
    // erasures waiting and nothing counted.
    private void Log<TState>(Action<ILogger, TState> log, TState state)
    {
        try
        {
            log(_logger, state);
        }
        catch (Exception)
        {
            // The event is lost; the metrics still count what it reports, and this counts the loss.
            RecordingTelemetry.LogFailed();
        }
    }

    // Faults a caller's task, and marks it observed: a caller that stopped waiting must not have the sink's exception,
    // which can quote a key or an id, reported to an UnobservedTaskException handler.
    private static void Fail(TaskCompletionSource completion, RecordingException exception)
    {
        if (completion.TrySetException(exception))
        {
            _ = completion.Task.Exception;
        }
    }

    private enum WorkKind
    {
        Record,
        Outcome,
        Erasure,
        Flush,
    }

    private sealed class WorkItem
    {
        private WorkItem(WorkKind kind, DecisionRecord? record, string? decisionId, DecisionOutcome? outcome, string? erasureKey, bool completes)
        {
            Kind = kind;
            Record = record;
            DecisionId = decisionId;
            Outcome = outcome;
            ErasureKey = erasureKey;
            Completion = completes ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
        }

        public WorkKind Kind { get; }

        public DecisionRecord? Record { get; }

        public string? DecisionId { get; }

        public DecisionOutcome? Outcome { get; }

        public string? ErasureKey { get; }

        // Set for an erasure and a flush, whose callers wait for the sink.
        public TaskCompletionSource? Completion { get; }

        private int _settled;

        // Claims the item's outcome, once: the writer after the sink call, or the dispose that gives up on it.
        public bool TrySettle() => Interlocked.Exchange(ref _settled, 1) == 0;

        public static WorkItem ForRecord(DecisionRecord record) => new(WorkKind.Record, record, null, null, null, completes: false);

        public static WorkItem ForOutcome(string decisionId, DecisionOutcome outcome) => new(WorkKind.Outcome, null, decisionId, outcome, null, completes: false);

        public static WorkItem ForErasure(string? erasureKey, string? decisionId) => new(WorkKind.Erasure, null, decisionId, null, erasureKey, completes: true);

        public static WorkItem ForFlush() => new(WorkKind.Flush, null, null, null, null, completes: true);
    }
}
