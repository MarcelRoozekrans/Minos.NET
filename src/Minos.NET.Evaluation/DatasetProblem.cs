namespace Minos.Evaluation;

/// <summary>One thing wrong with a dataset file, and where.</summary>
/// <param name="Line">The 1-based line of the file the problem is on.</param>
/// <param name="Message">
/// What is wrong. A failure from the core starts with its MIN id, then the path of the question, such as
/// <c>MIN003 'questions.urgent': </c>.
/// </param>
public sealed record DatasetProblem(int Line, string Message);
