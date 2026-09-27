using System.Globalization;
using System.Text;

namespace ZeroAlloc.Jev.Generator;

/// <summary>Escaping for the JSON and C# text the generator emits.</summary>
internal static class JsonText
{
    /// <summary>
    /// Appends <paramref name="value"/> as a JSON string. Every character outside printable ASCII becomes a
    /// <c>\uXXXX</c> escape, so the JSON is plain ASCII whatever the source file's encoding.
    /// </summary>
    public static StringBuilder AppendJsonString(this StringBuilder json, string value)
    {
        json.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    json.Append("\\\"");
                    break;
                case '\\':
                    json.Append("\\\\");
                    break;
                case '\n':
                    json.Append("\\n");
                    break;
                case '\r':
                    json.Append("\\r");
                    break;
                case '\t':
                    json.Append("\\t");
                    break;
                default:
                    if (c < ' ' || c > '~')
                    {
                        json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        json.Append(c);
                    }

                    break;
            }
        }

        return json.Append('"');
    }

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
                // Every character outside printable ASCII becomes a \uXXXX escape, matching
                // AppendJsonString's guarantee that the emitted text is plain ASCII whatever the source
                // file's encoding. This also covers U+2028 LINE SEPARATOR, U+2029 PARAGRAPH SEPARATOR and
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
