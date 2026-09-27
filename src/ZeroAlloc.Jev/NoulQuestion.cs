namespace ZeroAlloc.Jev;

/// <summary>A yes/no question. The answer is the probability that the answer is yes.</summary>
public sealed class NoulQuestion : JevQuestion
{
    /// <summary>Gets optional descriptions of what a yes and a no mean.</summary>
    public NoulCriteria? Criteria { get; init; }
}
