namespace Minos.Evaluation;

/// <summary>Reads the lines of text that is already decoded, skipping one leading byte order mark.</summary>
internal sealed class TextLineReader : ILineSource
{
    private readonly TextReader _reader;
    private bool _first = true;

    internal TextLineReader(TextReader reader) => _reader = reader;

    /// <inheritdoc />
    public bool TryReadLine(out string? line)
    {
        line = _reader.ReadLine();
        if (line is null)
        {
            return false;
        }

        if (_first)
        {
            _first = false;
            if (line.StartsWith('﻿'))
            {
                line = line[1..];
            }
        }

        return true;
    }
}
