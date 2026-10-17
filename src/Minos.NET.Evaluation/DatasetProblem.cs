namespace Minos.Evaluation;

/// <summary>One thing wrong with a dataset file, and where.</summary>
/// <param name="Line">
/// The 1-based line of the file the problem is on. For a problem from <see cref="CalibrationReport.Create"/>, it is the
/// line of the dataset item the problem is about, in the dataset file; 0 for a problem with the results header; and,
/// for a results item past the dataset's last item, the line after that last item, or 1 when the dataset is empty.
/// </param>
/// <param name="Message">
/// What is wrong. A failure from the core starts with its MIN id, then the path of the question, such as
/// <c>MIN003 'questions.urgent': </c>.
/// </param>
public sealed record DatasetProblem(int Line, string Message);
