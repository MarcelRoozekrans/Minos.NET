namespace ZeroAlloc.Jev;

/// <summary>A rule a question set built with <see cref="JevQuestionSetBuilder"/> breaks: a failure that stops the build, or a warning.</summary>
/// <param name="Rule">The rule's id: the same JEV id the analyzers report for a <c>[JevQuestions]</c> set, such as <c>JEV001</c>.</param>
/// <param name="QuestionKey">The wire key of the question that breaks the rule.</param>
/// <param name="Message">What is wrong, in a sentence.</param>
public sealed record JevQuestionFailure(string Rule, string? QuestionKey, string Message);
