using System.Text;

namespace ZeroAlloc.Jev.CodeFixes;

/// <summary>
/// Turns a PascalCase enum member name, with or without acronyms, into a sentence-case phrase for a generated
/// <c>[Criteria]</c> or <c>[Level]</c> description: <c>NeedsAttention</c> becomes <c>"Needs attention"</c> and
/// <c>HTTPError</c> becomes <c>"HTTP error"</c>.
/// </summary>
internal static class WordSplitter
{
    /// <summary>
    /// Splits <paramref name="identifier"/> into words and joins them with spaces. The first word keeps its
    /// original case; each later word is lowercased unless it is itself an acronym (two or more letters, all
    /// upper case), in which case it is left as written. A digit never starts or ends a word on its own: it stays
    /// attached to whichever letters are next to it.
    /// </summary>
    public static string ToSentence(string identifier)
    {
        var words = Split(identifier);
        if (words.Count == 0)
        {
            return identifier;
        }

        var builder = new StringBuilder(identifier.Length + words.Count - 1);
        for (var index = 0; index < words.Count; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            var word = words[index];
            builder.Append(index == 0 || IsAcronym(word) ? word : Lowercase(word));
        }

        return builder.ToString();
    }

    private static List<string> Split(string identifier)
    {
        var words = new List<string>();
        var start = 0;
        for (var index = 1; index < identifier.Length; index++)
        {
            if (IsWordBoundary(identifier, index))
            {
                words.Add(identifier.Substring(start, index - start));
                start = index;
            }
        }

        words.Add(identifier.Substring(start));
        return words;
    }

    /// <summary>Whether a new word starts at <paramref name="index"/>: a lower-to-upper transition
    /// (<c>needs|Attention</c>), or the last letter of an acronym run before a new word
    /// (<c>HTTP|Error</c>). Digits are neither upper nor lower, so they never trigger either rule and stay
    /// attached to the word they were written next to.</summary>
    private static bool IsWordBoundary(string identifier, int index)
    {
        var previous = identifier[index - 1];
        var current = identifier[index];

        if (char.IsUpper(current) && char.IsLower(previous))
        {
            return true;
        }

        return char.IsUpper(previous) && char.IsUpper(current)
            && index + 1 < identifier.Length && char.IsLower(identifier[index + 1]);
    }

    private static bool IsAcronym(string word)
    {
        var hasLetter = false;
        foreach (var c in word)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            hasLetter = true;
            if (!char.IsUpper(c))
            {
                return false;
            }
        }

        return hasLetter;
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
