using System.Text;

namespace Minos.CodeFixes;

/// <summary>
/// Turns an enum member name into a sentence-case phrase for a generated <c>[Criteria]</c> or <c>[Level]</c>
/// description: <c>NeedsAttention</c> becomes <c>"Needs attention"</c>, <c>HTTPError</c> becomes <c>"HTTP error"</c>
/// and <c>NEEDS_ATTENTION</c> becomes <c>"Needs attention"</c>.
/// </summary>
internal static class WordSplitter
{
    /// <summary>
    /// Splits <paramref name="identifier"/> into words and joins them with spaces.
    /// </summary>
    /// <remarks>
    /// <para>A new word starts at an upper-case letter that follows a lower-case letter or a digit
    /// (<c>Needs|Attention</c>, <c>Version2|Beta</c>), at the last upper-case letter of a run that a lower-case letter
    /// follows (<c>HTTP|Error</c>), and after each <c>_</c>, which is dropped. A digit never starts a word: it stays
    /// with the letters before it (<c>HTTP2|Error</c>, <c>X509|Cert</c>).</para>
    /// <para>The first word keeps its case. A later word is lowercased unless it is an acronym: two or more letters,
    /// all upper case, digits aside, so <c>IsAValue</c> becomes <c>"Is a value"</c>. A name with no lower-case letter
    /// that <c>_</c> separates is written in capitals as a style, not as acronyms: its first word is capitalized and
    /// every other letter lowercased.</para>
    /// </remarks>
    public static string ToSentence(string identifier)
    {
        var words = Split(identifier);
        if (words.Count == 0)
        {
            return identifier;
        }

        var screamingSnakeCase = identifier.IndexOf('_') >= 0 && !HasLowercase(identifier);
        var builder = new StringBuilder(identifier.Length);
        for (var index = 0; index < words.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            var word = words[index];
            if (screamingSnakeCase)
            {
                var lowercase = Lowercase(word);
                builder.Append(index == 0 ? char.ToUpperInvariant(lowercase[0]) + lowercase.Substring(1) : lowercase);
            }
            else
            {
                builder.Append(index == 0 || IsAcronym(word) ? word : Lowercase(word));
            }
        }

        return builder.ToString();
    }

    private static List<string> Split(string identifier)
    {
        var words = new List<string>();
        foreach (var part in identifier.Split('_'))
        {
            if (part.Length == 0)
            {
                continue;
            }

            var start = 0;
            for (var index = 1; index < part.Length; index++)
            {
                if (IsWordBoundary(part, index))
                {
                    words.Add(part.Substring(start, index - start));
                    start = index;
                }
            }

            words.Add(part.Substring(start));
        }

        return words;
    }

    /// <summary>Whether a new word starts at <paramref name="index"/>: an upper-case letter after a lower-case letter
    /// or a digit (<c>needs|Attention</c>, <c>2|Beta</c>), or the last letter of an upper-case run before a lower-case
    /// one (<c>HTTP|Error</c>).</summary>
    private static bool IsWordBoundary(string part, int index)
    {
        var previous = part[index - 1];
        var current = part[index];
        if (!char.IsUpper(current))
        {
            return false;
        }

        return char.IsLower(previous)
            || char.IsDigit(previous)
            || (char.IsUpper(previous) && index + 1 < part.Length && char.IsLower(part[index + 1]));
    }

    /// <summary>Whether <paramref name="word"/> is an acronym: two or more letters, every one upper case.</summary>
    private static bool IsAcronym(string word)
    {
        var letters = 0;
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            if (!char.IsUpper(c))
            {
                return false;
            }

            letters++;
        }

        return letters >= 2;
    }

    private static bool HasLowercase(string text)
    {
        foreach (var c in text)
        {
            if (char.IsLower(c))
            {
                return true;
            }
        }

        return false;
    }

    private static string Lowercase(string word)
    {
        var chars = word.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            chars[index] = char.ToLowerInvariant(chars[index]);
        }

        return new string(chars);
    }
}
