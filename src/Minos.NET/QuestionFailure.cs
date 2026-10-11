namespace Minos;

/// <summary>A rule a question set breaks: one a set built with <see cref="QuestionSetBuilder"/> breaks, which stops the build or warns, or one a provider's capabilities break, which returns <see cref="DecisionErrorKind.Unsupported"/> before a request is sent.</summary>
/// <param name="Rule">The rule's id: the MIN id the analyzers report for a <c>[Questions]</c> set, such as <c>MIN001</c>, or a provider capability rule from MIN201 up.</param>
/// <param name="QuestionKey">
/// The wire key of the question that breaks the rule, or <see langword="null"/> for a rule about the whole set, such as
/// MIN202, too many questions. Most rules are about one question.
/// </param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record QuestionFailure(string Rule, string? QuestionKey, string Message);
