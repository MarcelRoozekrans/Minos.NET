namespace ZeroAlloc.Jev;

/// <summary>The answer to a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulAnswer : JevAnswer
{
    /// <summary>Gets the answer on a scale from 0 (no) to 1 (yes).</summary>
    public required double Noul { get; init; }
}
