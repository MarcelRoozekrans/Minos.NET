using System.Globalization;
using System.Text;

namespace ZeroAlloc.Jev.Generator;

/// <summary>The outcome of <see cref="JsonMinifier.Minify"/>.</summary>
/// <param name="Json">The minified, ASCII-escaped JSON; <see langword="null"/> when the text is invalid.</param>
/// <param name="Strings">The decoded string values, not property names, in document order.</param>
/// <param name="Error">Why the text is invalid; <see langword="null"/> on success.</param>
/// <param name="ErrorOffset">The index of the offending token or character.</param>
internal sealed record MinifyResult(string? Json, EquatableArray<string> Strings, string? Error, int ErrorOffset)
{
    public bool Succeeded => Json is not null;
}

/// <summary>
/// Validates and minifies the text of a <c>Json = true</c> attribute argument by RFC 8259, strictly: no comments,
/// trailing commas, single quotes or byte order mark, no lone surrogates, at most <see cref="JevLimits.MaximumJsonDepth"/> levels of nesting, and an object
/// or array at the top level. Duplicate object keys are accepted, as RFC 8259 allows and System.Text.Json's
/// JsonDocument does, although its JsonObject rejects them. It has no dependencies: the generator runs inside the
/// compiler host, where System.Text.Json may not be loadable in a matching version.
/// </summary>
internal sealed class JsonMinifier
{
    private static readonly string DepthError =
        $"The JSON nests deeper than {JevLimits.MaximumJsonDepth.ToString(CultureInfo.InvariantCulture)} levels.";

    private readonly string _text;
    private readonly StringBuilder _output = new();
    private readonly List<string> _strings = [];
    private int _position;

    private JsonMinifier(string text) => _text = text;

    public static MinifyResult Minify(string text)
    {
        var minifier = new JsonMinifier(text);
        try
        {
            minifier.SkipWhitespace();
            if (minifier.Peek() is not ('{' or '['))
            {
                throw minifier.Fail("Expected a JSON object or array.");
            }

            minifier.ReadValue(depth: 0);
            minifier.SkipWhitespace();
            if (minifier._position < text.Length)
            {
                throw minifier.Fail("Unexpected content after the JSON value.");
            }

            return new MinifyResult(
                minifier._output.ToString(), new EquatableArray<string>(minifier._strings.ToArray()), null, 0);
        }
        catch (SyntaxError error)
        {
            return new MinifyResult(null, new EquatableArray<string>([]), error.Message, error.Offset);
        }
    }

    private char Peek() => _position < _text.Length ? _text[_position] : '\0';

    private bool AtEnd => _position >= _text.Length;

    private SyntaxError Fail(string message) => new(message, _position);

    private void SkipWhitespace()
    {
        while (!AtEnd && _text[_position] is ' ' or '\t' or '\n' or '\r')
        {
            _position++;
        }
    }

    private void ReadValue(int depth)
    {
        if (AtEnd)
        {
            throw Fail("Unexpected end of the JSON text.");
        }

        switch (Peek())
        {
            case '{':
                ReadObject(depth);
                break;
            case '[':
                ReadArray(depth);
                break;
            case '"':
                _strings.Add(ReadString());
                break;
            case 't':
                ReadLiteral("true");
                break;
            case 'f':
                ReadLiteral("false");
                break;
            case 'n':
                ReadLiteral("null");
                break;
            case '-':
            case >= '0' and <= '9':
                ReadNumber();
                break;
            default:
                throw Fail("Expected a JSON value.");
        }
    }

    private void ReadObject(int depth)
    {
        if (depth >= JevLimits.MaximumJsonDepth)
        {
            throw Fail(DepthError);
        }

        _output.Append('{');
        _position++;
        SkipWhitespace();
        if (Peek() == '}')
        {
            _output.Append('}');
            _position++;
            return;
        }

        while (true)
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw Fail("Unexpected end of the JSON text.");
            }

            if (Peek() != '"')
            {
                throw Fail("Expected a property name in double quotes.");
            }

            ReadString();
            SkipWhitespace();
            if (Peek() != ':')
            {
                throw Fail("Expected ':' after the property name.");
            }

            _output.Append(':');
            _position++;
            SkipWhitespace();
            ReadValue(depth + 1);
            SkipWhitespace();
            if (AtEnd)
            {
                throw Fail("Unexpected end of the JSON text.");
            }

            if (Peek() == ',')
            {
                _output.Append(',');
                _position++;
                continue;
            }

            if (Peek() == '}')
            {
                _output.Append('}');
                _position++;
                return;
            }

            throw Fail("Expected ',' or '}'.");
        }
    }

    private void ReadArray(int depth)
    {
        if (depth >= JevLimits.MaximumJsonDepth)
        {
            throw Fail(DepthError);
        }

        _output.Append('[');
        _position++;
        SkipWhitespace();
        if (Peek() == ']')
        {
            _output.Append(']');
            _position++;
            return;
        }

        while (true)
        {
            SkipWhitespace();
            ReadValue(depth + 1);
            SkipWhitespace();
            if (AtEnd)
            {
                throw Fail("Unexpected end of the JSON text.");
            }

            if (Peek() == ',')
            {
                _output.Append(',');
                _position++;
                continue;
            }

            if (Peek() == ']')
            {
                _output.Append(']');
                _position++;
                return;
            }

            throw Fail("Expected ',' or ']'.");
        }
    }

    /// <summary>Reads a string token, writes it re-escaped as ASCII, and returns its decoded value.</summary>
    private string ReadString()
    {
        _position++;
        var value = new StringBuilder();
        while (true)
        {
            if (AtEnd)
            {
                throw Fail("The string is not closed.");
            }

            var c = _text[_position];
            if (c == '"')
            {
                _position++;
                break;
            }

            if (c < ' ')
            {
                throw Fail("A control character in a string must be escaped.");
            }

            if (c == '\\')
            {
                var escapeStart = _position;
                var decoded = ReadEscape();
                if (char.IsHighSurrogate(decoded))
                {
                    if (_position + 1 < _text.Length && _text[_position] == '\\' && _text[_position + 1] == 'u')
                    {
                        var low = ReadEscape();
                        if (!char.IsLowSurrogate(low))
                        {
                            throw new SyntaxError("A high surrogate is not followed by a low surrogate.", escapeStart);
                        }

                        value.Append(decoded).Append(low);
                        continue;
                    }

                    throw new SyntaxError("A high surrogate is not followed by a low surrogate.", escapeStart);
                }

                if (char.IsLowSurrogate(decoded))
                {
                    throw new SyntaxError("A low surrogate has no high surrogate before it.", escapeStart);
                }

                value.Append(decoded);
                continue;
            }

            if (char.IsHighSurrogate(c) && _position + 1 < _text.Length && char.IsLowSurrogate(_text[_position + 1]))
            {
                value.Append(c).Append(_text[_position + 1]);
                _position += 2;
                continue;
            }

            if (char.IsSurrogate(c))
            {
                throw Fail("The text holds a lone surrogate.");
            }

            value.Append(c);
            _position++;
        }

        var text = value.ToString();
        _output.AppendJsonString(text);
        return text;
    }

    /// <summary>Reads one escape sequence starting at its backslash and returns the character it stands for.</summary>
    private char ReadEscape()
    {
        _position++;
        if (AtEnd)
        {
            throw Fail("The string is not closed.");
        }

        var c = _text[_position];
        _position++;
        switch (c)
        {
            case '"': return '"';
            case '\\': return '\\';
            case '/': return '/';
            case 'b': return '\b';
            case 'f': return '\f';
            case 'n': return '\n';
            case 'r': return '\r';
            case 't': return '\t';
            case 'u':
                var code = 0;
                for (var i = 0; i < 4; i++)
                {
                    var digit = AtEnd ? -1 : HexValue(_text[_position]);
                    if (digit < 0)
                    {
                        throw Fail("A \\u escape needs four hexadecimal digits.");
                    }

                    code = (code * 16) + digit;
                    _position++;
                }

                return (char)code;
            default:
                _position--;
                throw Fail("Unknown escape sequence.");
        }
    }

    private static int HexValue(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private void ReadLiteral(string literal)
    {
        if (_position + literal.Length > _text.Length
            || string.CompareOrdinal(_text, _position, literal, 0, literal.Length) != 0)
        {
            throw Fail("Expected a JSON value.");
        }

        _output.Append(literal);
        _position += literal.Length;
    }

    /// <summary>Reads <c>-? (0 | [1-9][0-9]*) (. [0-9]+)? ([eE] [+-]? [0-9]+)?</c> and copies it as written.</summary>
    private void ReadNumber()
    {
        var start = _position;
        if (Peek() == '-')
        {
            _position++;
        }

        if (Peek() == '0')
        {
            _position++;
        }
        else if (Peek() is >= '1' and <= '9')
        {
            SkipDigits();
        }
        else
        {
            throw Fail("Expected a digit.");
        }

        if (Peek() == '.')
        {
            _position++;
            RequireDigits();
        }

        if (Peek() is 'e' or 'E')
        {
            _position++;
            if (Peek() is '+' or '-')
            {
                _position++;
            }

            RequireDigits();
        }

        _output.Append(_text, start, _position - start);
    }

    private void RequireDigits()
    {
        if (Peek() is not (>= '0' and <= '9'))
        {
            throw Fail("Expected a digit.");
        }

        SkipDigits();
    }

    private void SkipDigits()
    {
        while (Peek() is >= '0' and <= '9')
        {
            _position++;
        }
    }

    private sealed class SyntaxError(string message, int offset) : Exception(message)
    {
        public int Offset { get; } = offset;
    }
}
