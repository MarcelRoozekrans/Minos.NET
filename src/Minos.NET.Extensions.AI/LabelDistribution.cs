namespace Minos.Extensions.AI;

/// <summary>Turns label logprobs or stated probabilities into a distribution over the labels A, B, C and on.</summary>
internal static class LabelDistribution
{
    private const int MaxLabels = 26;

    /// <summary>The label for an option index: 0 is "A", 25 is "Z".</summary>
    internal static string Label(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxLabels);
        return ((char)('A' + index)).ToString();
    }

    /// <summary>
    /// Sums the probability of every token whose trimmed text is a label. Labels with no token share what is left
    /// of 1 equally. Returns null when no token is a label.
    /// </summary>
    internal static double[]? FromLogprobs(int labelCount, IEnumerable<(string Token, double Logprob)> top)
    {
        var p = new double[labelCount];
        var any = false;
        foreach (var (token, logprob) in top)
        {
            var trimmed = token.Trim();
            for (var i = 0; i < labelCount; i++)
            {
                if (string.Equals(trimmed, Label(i), StringComparison.Ordinal))
                {
                    p[i] += Math.Exp(logprob);
                    any = true;
                    break;
                }
            }
        }

        if (!any)
        {
            return null;
        }

        var missing = 0;
        var sum = 0.0;
        foreach (var value in p)
        {
            sum += value;
            if (value == 0)
            {
                missing++;
            }
        }

        if (missing > 0)
        {
            var share = Math.Max(0, 1 - sum) / missing;
            for (var i = 0; i < p.Length; i++)
            {
                if (p[i] == 0)
                {
                    p[i] = share;
                    sum += share;
                }
            }
        }

        return Normalize(p, sum);
    }

    /// <summary>Normalizes the model's stated probabilities to sum 1. Returns null when they are malformed.</summary>
    internal static double[]? FromStated(int labelCount, IReadOnlyList<double> stated)
    {
        if (stated.Count != labelCount)
        {
            return null;
        }

        var p = new double[labelCount];
        var sum = 0.0;
        for (var i = 0; i < labelCount; i++)
        {
            var value = stated[i];
            if (!double.IsFinite(value) || value < 0)
            {
                return null;
            }

            p[i] = value;
            sum += value;
        }

        return sum > 0 && double.IsFinite(sum) ? Normalize(p, sum) : null;
    }

    /// <summary>The most likely option and its probability. A tie takes the lower index.</summary>
    internal static (int Picked, double Confidence) Pick(double[] p)
    {
        var picked = 0;
        for (var i = 1; i < p.Length; i++)
        {
            if (p[i] > p[picked])
            {
                picked = i;
            }
        }

        return (picked, p[picked]);
    }

    /// <summary>The probability-weighted level, where option i is level i.</summary>
    internal static double Expected(double[] p)
    {
        var expected = 0.0;
        for (var i = 0; i < p.Length; i++)
        {
            expected += i * p[i];
        }

        return expected;
    }

    private static double[] Normalize(double[] p, double sum)
    {
        for (var i = 0; i < p.Length; i++)
        {
            p[i] /= sum;
        }

        return p;
    }
}
