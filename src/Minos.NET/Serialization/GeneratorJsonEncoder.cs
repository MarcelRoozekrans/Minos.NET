using System.Globalization;
using System.Text.Encodings.Web;

namespace Minos.Serialization;

/// <summary>
/// Escapes JSON text exactly as the <c>[Questions]</c> generator's <c>JsonText.AppendJsonString</c> does, so a
/// question set built at run time writes the same bytes as a generated one: <c>\"</c>, <c>\\</c>, <c>\n</c>, <c>\r</c>
/// and <c>\t</c>; every other character outside printable ASCII as a lowercase <c>\uXXXX</c> escape, a character
/// outside the Basic Multilingual Plane as its surrogate pair's two escapes; printable ASCII as is. A lone surrogate
/// reaches the encoder as U+FFFD, which it writes as <c>\ufffd</c>, as the generator does.
/// </summary>
internal sealed class GeneratorJsonEncoder : JavaScriptEncoder
{
    /// <summary>The one instance; it is stateless and safe to share.</summary>
    public static readonly GeneratorJsonEncoder Instance = new();

    private GeneratorJsonEncoder()
    {
    }

    // A surrogate pair's two six-character escapes.
    public override int MaxOutputCharactersPerInputCharacter => 12;

    public override bool WillEncode(int unicodeScalar) => NeedsEscape(unicodeScalar);

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        for (var i = 0; i < textLength; i++)
        {
            if (NeedsEscape(text[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public override unsafe bool TryEncodeUnicodeScalar(int unicodeScalar, char* buffer, int bufferLength, out int numberOfCharactersWritten)
    {
        var output = new Span<char>(buffer, bufferLength);
        switch (unicodeScalar)
        {
            case '"':
                return TryWrite(output, "\\\"", out numberOfCharactersWritten);
            case '\\':
                return TryWrite(output, "\\\\", out numberOfCharactersWritten);
            case '\n':
                return TryWrite(output, "\\n", out numberOfCharactersWritten);
            case '\r':
                return TryWrite(output, "\\r", out numberOfCharactersWritten);
            case '\t':
                return TryWrite(output, "\\t", out numberOfCharactersWritten);
            case > 0xFFFF:
                numberOfCharactersWritten = 0;
                if (output.Length < 12)
                {
                    return false;
                }

                var offset = unicodeScalar - 0x10000;
                WriteEscape(output, (char)(0xD800 + (offset >> 10)));
                WriteEscape(output[6..], (char)(0xDC00 + (offset & 0x3FF)));
                numberOfCharactersWritten = 12;
                return true;
            default:
                numberOfCharactersWritten = 0;
                if (output.Length < 6)
                {
                    return false;
                }

                WriteEscape(output, (char)unicodeScalar);
                numberOfCharactersWritten = 6;
                return true;
        }
    }

    private static bool NeedsEscape(int c) => c is '"' or '\\' or < ' ' or > '~';

    private static bool TryWrite(Span<char> output, string escape, out int written)
    {
        if (escape.Length > output.Length)
        {
            written = 0;
            return false;
        }

        escape.CopyTo(output);
        written = escape.Length;
        return true;
    }

    private static void WriteEscape(Span<char> output, char c)
    {
        output[0] = '\\';
        output[1] = 'u';
        ((int)c).TryFormat(output.Slice(2, 4), out _, "x4", CultureInfo.InvariantCulture);
    }
}
