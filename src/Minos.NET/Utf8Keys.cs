using System.Text;
using System.Text.Json;

namespace Minos;

/// <summary>
/// The UTF-8 key table and the scan over it that every run-time question set shares: the option sets of a question and
/// the question keys of a set look their wire keys up the same way.
/// </summary>
internal static class Utf8Keys
{
    /// <summary>Encodes <paramref name="keys"/> as UTF-8, once, for <see cref="IndexOf"/>.</summary>
    /// <param name="keys">The wire keys.</param>
    /// <returns>One byte array per key, in the same order.</returns>
    public static byte[][] Encode(string[] keys)
    {
        var utf8Keys = new byte[keys.Length][];
        for (var i = 0; i < keys.Length; i++)
        {
            utf8Keys[i] = Encoding.UTF8.GetBytes(keys[i]);
        }

        return utf8Keys;
    }

    /// <summary>
    /// Returns the position of the key equal to the reader's current token, by a linear, allocation-free
    /// <see cref="Utf8JsonReader.ValueTextEquals(ReadOnlySpan{byte})"/> scan, which unescapes the token as it compares.
    /// </summary>
    /// <param name="reader">A reader positioned on a <see cref="JsonTokenType.PropertyName"/> or <see cref="JsonTokenType.String"/> token.</param>
    /// <param name="keys">The keys, from <see cref="Encode"/>.</param>
    /// <returns>The position, or -1 when no key matches.</returns>
    public static int IndexOf(ref Utf8JsonReader reader, byte[][] keys)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            if (reader.ValueTextEquals(keys[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
