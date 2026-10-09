using System.Diagnostics;
using System.Globalization;
using ZeroAlloc.Validation;

namespace Minos.Validation;

/// <summary>Checks a set built at run time and turns ZeroAlloc.Validation's failures into <see cref="JevQuestionFailure"/>s.</summary>
internal static class QuestionValidation
{
    private const string QuestionPrefix = "Questions[";

    // Generated validators are stateless: one instance serves every build, on every thread.
    private static readonly QuestionSetSpecValidator Validator = new(new QuestionSpecValidator());

    /// <summary>Checks <paramref name="questions"/> against the API's rules.</summary>
    /// <param name="questions">The questions, in wire order.</param>
    /// <returns>The failures, which make the set invalid, and the warnings, which do not.</returns>
    public static (JevQuestionFailure[] Failures, JevQuestionFailure[] Warnings) Validate(QuestionSpec[] questions)
    {
        var result = Validator.Validate(new QuestionSetSpec { Questions = questions });
        // ZeroAlloc.Validation reports IsValid == false when any failure exists, warnings included, so none are dropped here;
        // BlankInstructions_WarnJev003 in the tests pins that a warnings-only set returns its warnings.
        if (result.IsValid)
        {
            return ([], []);
        }

        var failures = new List<JevQuestionFailure>();
        var warnings = new List<JevQuestionFailure>();
        foreach (ref readonly var failure in result.Failures)
        {
            var item = new JevQuestionFailure(
                failure.ErrorCode ?? throw new UnreachableException("Every question rule names its JEV id."),
                KeyOf(questions, failure.PropertyName),
                failure.ErrorMessage);
            (failure.Severity == Severity.Error ? failures : warnings).Add(item);
        }

        return ([.. failures], [.. warnings]);
    }

    // Every failure is about one question, and its path starts with "Questions[i]".
    private static string? KeyOf(QuestionSpec[] questions, string propertyName)
    {
        if (!propertyName.StartsWith(QuestionPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var end = propertyName.IndexOf(']', QuestionPrefix.Length);
        return end > QuestionPrefix.Length
            && int.TryParse(propertyName.AsSpan(QuestionPrefix.Length, end - QuestionPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            && index < questions.Length
                ? questions[index].Key
                : null;
    }
}
