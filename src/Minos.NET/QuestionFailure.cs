namespace Minos;

/// <summary>A rule a question set built with <see cref="QuestionSetBuilder"/> breaks: a failure that stops the build, or a warning.</summary>
/// <param name="Rule">The rule's id: the same JEV id the analyzers report for a <c>[Questions]</c> set, such as <c>MIN001</c>.</param>
/// <param name="QuestionKey">
/// The wire key of the question that breaks the rule, or <see langword="null"/> for a rule about the whole set. Every
/// rule today is about one question.
/// </param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record QuestionFailure(string Rule, string? QuestionKey, string Message);
