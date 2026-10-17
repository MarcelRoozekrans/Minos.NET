using System.Buffers;
using System.Text;

namespace Minos.Evaluation;

// The value rules of the results file, shared by the writer and the reader so that what Save accepts, Load accepts.
internal static class ResultsRules
{
    internal static bool IsUnit(double value) => double.IsFinite(value) && value is >= 0 and <= 1;

    // Null is fine here: whether a member may be null is its own rule. Text with an unpaired surrogate cannot be written as UTF-8.
    internal static bool IsValidText(string? text)
    {
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(rest, out _, out var used) != OperationStatus.Done)
            {
                return false;
            }

            rest = rest[used..];
        }

        return true;
    }

    // Replaces each unpaired surrogate with U+FFFD, for text a provider supplied.
    internal static string? Clean(string? text)
    {
        if (text is null || IsValidText(text))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var rest = text.AsSpan();
        while (!rest.IsEmpty)
        {
            var rune = Rune.DecodeFromUtf16(rest, out var value, out var used) == OperationStatus.Done ? value : Rune.ReplacementChar;
            builder.Append(rune.ToString());
            rest = rest[used..];
        }

        return builder.ToString();
    }

    // A Noul answer's flag and confidence are derived from its probability, as the runner writes them: the flag is
    // p >= 0.5 and the confidence is max(p, 1 - p). A saved answer that disagrees would score accuracy from one
    // number and calibration from another.
    internal const double NoulConfidenceTolerance = 1e-9;

    internal static bool IsConsistentNoul(bool value, double probability, double confidence)
        => value == (probability >= 0.5) && Math.Abs(confidence - Math.Max(probability, 1 - probability)) <= NoulConfidenceTolerance;

    internal const string NoulConsistencyRule = "agree with its probability: the value is probability >= 0.5 and the confidence is max(p, 1 - p)";

    internal static bool IsNonNegative(double value) => double.IsFinite(value) && value >= 0;

    // The smallest allowed values of the integer members.
    internal const int MinItemCount = 0;
    internal const int MinParallelism = 1;
    internal const int MinLine = 1;
    internal const int MinTokens = 0;
    internal const int MinScoreLevel = 0;
}
