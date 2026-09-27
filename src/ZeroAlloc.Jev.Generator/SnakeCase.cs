using System.Globalization;
using System.Text;

namespace ZeroAlloc.Jev.Generator;

/// <summary>snake_case conversion identical to <c>JsonNamingPolicy.SnakeCaseLower</c>.</summary>
internal static class SnakeCase
{
    private enum State
    {
        NotStarted,
        UppercaseLetter,
        LowercaseLetterOrDigit,
        SpaceSeparator,
    }

    public static string Convert(string name)
    {
        var result = new StringBuilder(name.Length + (name.Length / 4));
        var state = State.NotStarted;

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];
            switch (char.GetUnicodeCategory(current))
            {
                case UnicodeCategory.UppercaseLetter:
                    if (state is State.LowercaseLetterOrDigit or State.SpaceSeparator
                        || (state == State.UppercaseLetter && i + 1 < name.Length && char.IsLower(name[i + 1])))
                    {
                        result.Append('_');
                    }

                    result.Append(char.ToLowerInvariant(current));
                    state = State.UppercaseLetter;
                    break;

                case UnicodeCategory.LowercaseLetter:
                case UnicodeCategory.DecimalDigitNumber:
                    if (state == State.SpaceSeparator)
                    {
                        result.Append('_');
                    }

                    result.Append(current);
                    state = State.LowercaseLetterOrDigit;
                    break;

                case UnicodeCategory.SpaceSeparator:
                    if (state != State.NotStarted)
                    {
                        state = State.SpaceSeparator;
                    }

                    break;

                default:
                    result.Append(current);
                    state = State.NotStarted;
                    break;
            }
        }

        return result.ToString();
    }
}
