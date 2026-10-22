using System.Buffers;

namespace Minos.Recording;

/// <summary>
/// A sink that appends to a JSONL file, one line per record, outcome and erasure, in the format
/// <see cref="DecisionRecordFile"/> reads. It is for development and tests, as <see cref="InMemoryDecisionRecordSink"/>
/// is: a production sink deletes erased records for real.
/// </summary>
/// <remarks>
/// <para>
/// An erasure appends a tombstone line. A read applies it to the records before it, but the erased state stays in the
/// file until <see cref="DecisionRecordFile.CompactAsync(string, CancellationToken)"/> rewrites the file without it. The
/// compaction needs every sink on the file closed: dispose the recorder and the sink, compact, then open them again.
/// </para>
/// <para>
/// Each line goes to the operating system in one write before the call returns. A record or an outcome line is not
/// forced to disk; an erasure line is, because a returned erasure is its caller's confirmation. The file is opened for
/// appending when the sink is created, and other processes may read it meanwhile. When the file's last line was cut, by
/// a crash or a failed write, the next line starts on a line of its own, so it is still read. On Unix, a file the sink
/// creates can be read and written by its owner only, because records hold state. Dispose the sink after the recorder
/// that writes to it: the recorder does not.
/// </para>
/// </remarks>
public sealed class JsonlDecisionRecordSink : IDecisionRecordSink, IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private bool _broken;
    private bool _disposed;

    /// <summary>Gets or sets how an erasure line is forced to disk; a test replaces it to see that it is.</summary>
    internal Action<FileStream> FlushToDisk { get; set; } = static stream => stream.Flush(flushToDisk: true);

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
        try
        {
            // A crash can have cut the file's last line. The first line written now must not be glued onto it, or an
            // erasure would be lost with the fragment.
            _broken = !EndsAtALineEnd(path, _stream.Length);
        }
        catch
        {
            _stream.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return AppendAsync(buffer => DecisionRecordFile.WriteRecordLine(buffer, record), durable: false, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        return AppendAsync(buffer => DecisionRecordFile.WriteOutcomeLine(buffer, decisionId, outcome), durable: false, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        return AppendAsync(buffer => DecisionRecordFile.WriteErasureLine(buffer, erasureKey, null), durable: true, cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        return AppendAsync(buffer => DecisionRecordFile.WriteErasureLine(buffer, null, decisionId), durable: true, cancellationToken);
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

    private static bool EndsAtALineEnd(string path, long length)
    {
        if (length == 0)
        {
            return true;
        }

        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        Span<byte> last = stackalloc byte[1];
        return RandomAccess.Read(handle, last, length - 1) == 1 && last[0] == (byte)'\n';
    }

    // An erasure line is forced to disk before the call returns: the erasure's caller takes the return as its confirmation.
    private async ValueTask AppendAsync(Action<ArrayBufferWriter<byte>> write, bool durable, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer.ResetWrittenCount();
        if (_broken)
        {
            // A write that failed may have left part of a line: end it, so the next line is read on its own.
            _buffer.GetSpan(1)[0] = (byte)'\n';
            _buffer.Advance(1);
        }

        write(_buffer);
        _buffer.GetSpan(1)[0] = (byte)'\n';
        _buffer.Advance(1);
        _broken = true;
        await _stream.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _broken = false;
        if (durable)
        {
            FlushToDisk(_stream);
        }
    }
}
