using System.Globalization;
using Minos.Generator;
using ZeroAlloc.Validation;

namespace Minos.Validation;

/// <summary>
/// A whole set built at run time. Each question is checked by the <see cref="QuestionSpec"/> rules, which
/// ZeroAlloc.Validation reports under <c>Questions[i]</c>; the set adds MIN106 for a question key used more than once.
/// </summary>
[Validate]
internal sealed record QuestionSetSpec
{
    public required QuestionSpec[] Questions { get; init; }

    /// <summary>MIN106 for a repeated question key, reported once per key, at its second use. An empty key is QuestionSpec's.</summary>
    [CustomValidation]
    public ValidationFailure[] ValidateKeys()
    {
        List<ValidationFailure>? failures = null;
        for (var i = 0; i < Questions.Length; i++)
        {
            var key = Questions[i].Key;
            if (key.Length == 0 || CountBefore(i, key) != 1)
            {
                continue;
            }

            (failures ??= []).Add(new ValidationFailure
            {
                PropertyName = "Questions[" + i.ToString(CultureInfo.InvariantCulture) + "].Key",
                ErrorMessage = $"The question key '{key}' is used more than once.",
                ErrorCode = DiagnosticIds.DuplicateKey,
            });
        }

        return failures?.ToArray() ?? [];
    }

    private int CountBefore(int index, string key)
    {
        var count = 0;
        foreach (ref readonly var other in Questions.AsSpan(0, index))
        {
            if (string.Equals(other.Key, key, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}
