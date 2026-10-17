namespace Minos.Evaluation;

/// <summary>One bin of the reliability table.</summary>
public sealed class ReliabilityBin
{
    internal ReliabilityBin(int count, double meanConfidence, double accuracy)
    {
        Count = count;
        MeanConfidence = meanConfidence;
        Accuracy = accuracy;
    }

    /// <summary>Gets the number of answers in the bin.</summary>
    public int Count { get; }

    /// <summary>Gets the mean confidence of those answers.</summary>
    public double MeanConfidence { get; }

    /// <summary>Gets the share of those answers that are correct.</summary>
    public double Accuracy { get; }
}
