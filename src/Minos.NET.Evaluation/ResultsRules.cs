namespace Minos.Evaluation;

// The value rules of the results file, shared by the writer and the reader so that what Save accepts, Load accepts.
internal static class ResultsRules
{
    internal static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    internal static bool IsNonNegative(double value) => double.IsFinite(value) && value >= 0;

    // The smallest allowed values of the integer members.
    internal const int MinItemCount = 0;
    internal const int MinParallelism = 1;
    internal const int MinLine = 1;
    internal const int MinTokens = 0;
    internal const int MinScoreLevel = 0;
}
