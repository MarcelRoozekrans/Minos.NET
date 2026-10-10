using System.Globalization;
using System.Text;

namespace Minos.Generator;

/// <summary>Escaping for the C# text the generator emits.</summary>
internal static class CSharpText
{
    /// <summary>Returns <paramref name="value"/> as a regular C# string literal, for use as a string or with the <c>u8</c> suffix.</summary>
    public static string CSharpLiteral(string value)
    {
        var literal = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            if (c == '"' || c == '\\')
            {
                literal.Append('\\').Append(c);
            }
            else if (c < ' ' || c > '~')
            {
                // Every character outside printable ASCII becomes a \uXXXX escape, so the emitted text is
                // plain ASCII whatever the source file's encoding. This also covers U+2028 LINE SEPARATOR, U+2029 PARAGRAPH SEPARATOR and
                // U+0085 NEXT LINE, which are treated as newlines inside a regular C# string literal even
                // though they are not '\n' or '\r', so a raw one here would produce CS1010 "Newline in
                // constant" in the generated file.
                literal.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                literal.Append(c);
            }
        }

        return literal.Append('"').ToString();
    }
}
