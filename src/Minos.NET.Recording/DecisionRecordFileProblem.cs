namespace Minos.Recording;

/// <summary>A line of a record file that could not be used, and why. The message never holds a decision id or state.</summary>
/// <param name="Line">The 1-based line number.</param>
/// <param name="Message">What is wrong with the line.</param>
public readonly record struct DecisionRecordFileProblem(int Line, string Message);
