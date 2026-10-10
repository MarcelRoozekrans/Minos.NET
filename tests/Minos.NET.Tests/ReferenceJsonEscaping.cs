using System.Globalization;
using System.Text;

namespace Minos.Tests;

/// <summary>
/// The JSON string escaping the <c>[Questions]</c> generator wrote into its <c>questions</c> literals before Phase 6.2,
/// kept as the reference the runtime encoder must match byte for byte, so the wire stays unchanged.
/// </summary>
internal static class ReferenceJsonEscaping
{
    /// <summary>
    /// Appends <paramref name="value"/> as a JSON string. Every character outside printable ASCII becomes a
    /// <c>\uXXXX</c> escape, so the JSON is plain ASCII. A lone surrogate becomes <c>\ufffd</c>.
    /// </summary>
    public static StringBuilder AppendJsonString(this StringBuilder json, string value)
    {
        json.Append('"');
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
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
                    if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        AppendEscape(json, c);
                        AppendEscape(json, value[++i]);
                    }
                    else if (char.IsSurrogate(c))
                    {
                        // A lone surrogate is not text: System.Text.Json rejects its escape, so send the replacement
                        // character, as the .NET UTF-8 encoder does.
                        json.Append("\\ufffd");
                    }
                    else if (c < ' ' || c > '~')
                    {
                        AppendEscape(json, c);
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

    private static void AppendEscape(StringBuilder json, char c)
        => json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
}
