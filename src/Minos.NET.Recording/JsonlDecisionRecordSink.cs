using System.Buffers;
using System.Text.Json;
using Minos.Recording.RecordFormat;

namespace Minos.Recording;

/// <summary>
/// A sink that appends to a JSONL file, one line per record, outcome and erasure, in the format
/// <see cref="DecisionRecordFile"/> reads. An erasure appends a line that removes the records before it when the file is
/// read; <see cref="DecisionRecordFile.CompactAsync(string, CancellationToken)"/> rewrites the file without them. Each
/// line goes to the operating system in one write before the call returns, but is not forced to disk. The file is
/// opened for appending when the sink is created, and other processes may read it meanwhile. When the file's last line
/// was cut, by a crash or a failed write, the next line starts on a line of its own, so it is still read. On Unix, a file the sink
/// creates can be read and written by its owner only, because records hold state. Dispose the sink after the recorder
/// that writes to it: the recorder does not.
/// </summary>
public sealed class JsonlDecisionRecordSink : IDecisionRecordSink, IAsyncDisposable
{
    private readonly FileStream _stream;
    private readonly ArrayBufferWriter<byte> _buffer = new();
    private bool _broken;
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
        return AppendAsync(w => RecordLine.Write(w, record), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        ArgumentNullException.ThrowIfNull(outcome);
        return AppendAsync(w => RecordLine.WriteOutcome(w, decisionId, outcome), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        return AppendAsync(w => RecordLine.WriteErasure(w, erasureKey, null), cancellationToken);
    }

    /// <inheritdoc/>
    /// <exception cref="ObjectDisposedException">The sink is disposed.</exception>
    public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(decisionId);
        return AppendAsync(w => RecordLine.WriteErasure(w, null, decisionId), cancellationToken);
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

    private async ValueTask AppendAsync(Action<Utf8JsonWriter> write, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _buffer.ResetWrittenCount();
        if (_broken)
        {
            // A write that failed may have left part of a line: end it, so the next line is read on its own.
            _buffer.GetSpan(1)[0] = (byte)'\n';
            _buffer.Advance(1);
        }

        RecordLine.AppendLine(_buffer, write);
        _broken = true;
        await _stream.WriteAsync(_buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _broken = false;
    }
}
