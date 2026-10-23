using System.Buffers;

namespace Minos.Recording;

/// <summary>
/// A sink that appends to a JSONL file, one line per record, outcome and erasure, in the format
/// <see cref="DecisionRecordFile"/> reads. It is for development and tests, as <see cref="InMemoryDecisionRecordSink"/>
/// is: a production sink deletes erased records for real.
/// </summary>
/// <remarks>
/// <para>
/// An erasure appends a tombstone line: a read applies it to the records before it, but the erased state stays in the
/// file. To purge erased state in development, delete the file.
/// </para>
/// <para>
/// Each line goes to the operating system in one write, flushed before the call returns; it is not forced to disk. A
/// crash can leave the last line cut. A read reports it as a problem and skips it, and with it the first line a sink
/// appends to the file afterwards, which joins the cut one. The file is opened for appending when
/// the sink is created, and other processes may read it meanwhile. On Unix, a file the sink creates can be read and
/// written by its owner only, because records hold state. Dispose the sink after the recorder that writes to it: the
/// recorder does not.
/// </para>
/// </remarks>
public sealed class JsonlDecisionRecordSink : IDecisionRecordSink, IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private bool _disposed;

    /// <summary>Opens the file for appending, and creates it when it does not exist.</summary>
    /// <param name="path">The path of the file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty.</exception>
    /// <exception cref="IOException">The file could not be opened.</exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be written.</exception>
    public JsonlDecisionRecordSink(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var options = new FileStreamOptions
        {
            Mode = FileMode.Append,
            Access = FileAccess.Write,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous,
            BufferSize = 0,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        _stream = new FileStream(path, options);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AppendAsync(buffer => DecisionRecordFile.WriteRecordLine(buffer, record), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        return AppendAsync(buffer => DecisionRecordFile.WriteOutcomeLine(buffer, decisionId, outcome), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        return AppendAsync(buffer => DecisionRecordFile.WriteErasureLine(buffer, erasureKey, null), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        return AppendAsync(buffer => DecisionRecordFile.WriteErasureLine(buffer, null, decisionId), cancellationToken);
    }

    /// <summary>Closes the file.</summary>
    /// <returns>A task that completes when the file is closed.</returns>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        return _stream.DisposeAsync();
    }

    // Writes one line and its line feed in one write, and flushes it before the call returns.
    private async ValueTask AppendAsync(Action<ArrayBufferWriter<byte>> write, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer.ResetWrittenCount();
        write(_buffer);
        _buffer.Write("\n"u8);
        await _stream.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
