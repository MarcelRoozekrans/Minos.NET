using System.Text;
using System.Text.Unicode;

namespace Minos.Evaluation;

/// <summary>
/// Reads the lines of a UTF-8 stream without replacing bad bytes. Lines end at <c>\n</c>, <c>\r</c> or <c>\r\n</c>,
/// as <see cref="TextReader.ReadLine"/> has it, and a leading UTF-8 byte order mark is skipped. Each line is decoded on
/// its own, so a line that is not valid UTF-8 does not stop the lines after it from being read.
/// </summary>
internal sealed class Utf8LineReader : ILineSource
{
    private readonly Stream _stream;
    private byte[] _buffer = new byte[16 * 1024];
    private int _start;
    private int _end;
    private bool _endOfStream;
    private bool _skipLineFeed;
    private bool _first = true;

    internal Utf8LineReader(Stream stream) => _stream = stream;

    /// <inheritdoc />
    public bool TryReadLine(out string? line)
    {
        // The bytes of the current line already searched for a line break, so a long line is not searched again.
        var searched = 0;
        while (true)
        {
            if (_skipLineFeed && _end > _start)
            {
                // The line before ended with '\r'; a '\n' right after it belongs to the same break.
                if (_buffer[_start] == (byte)'\n')
                {
                    _start++;
                }

                _skipLineFeed = false;
            }

            if (!_skipLineFeed)
            {
                var index = _buffer.AsSpan(_start + searched, _end - _start - searched).IndexOfAny((byte)'\r', (byte)'\n');
                if (index >= 0)
                {
                    var length = searched + index;
                    _skipLineFeed = _buffer[_start + length] == (byte)'\r';
                    line = Decode(_buffer.AsSpan(_start, length));
                    _start += length + 1;
                    return true;
                }

                searched = _end - _start;
            }

            if (_endOfStream)
            {
                _skipLineFeed = false;
                if (_end == _start)
                {
                    line = null;
                    return false;
                }

                line = Decode(_buffer.AsSpan(_start, _end - _start));
                _start = _end;
                return true;
            }

            Fill();
        }
    }

    private void Fill()
    {
        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            _end -= _start;
            _start = 0;
        }

        if (_end == _buffer.Length)
        {
            Array.Resize(ref _buffer, _buffer.Length * 2);
        }

        var read = _stream.Read(_buffer, _end, _buffer.Length - _end);
        if (read == 0)
        {
            _endOfStream = true;
        }
        else
        {
            _end += read;
        }
    }

    // Null when the line is not valid UTF-8: the reader reports it instead of reading U+FFFD in its place.
    private string? Decode(ReadOnlySpan<byte> bytes)
    {
        if (_first)
        {
            _first = false;
            if (bytes.StartsWith("﻿"u8))
            {
                bytes = bytes[3..];
            }
        }

        return Utf8.IsValid(bytes) ? Encoding.UTF8.GetString(bytes) : null;
    }
}
