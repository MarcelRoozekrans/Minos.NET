namespace Minos.Recording;

/// <summary>
/// What <see cref="RecordingOptions.Redact"/> decided for a decision's state: keep a record with the given state, or keep
/// no record at all.
/// </summary>
public sealed class RedactionResult
{
    private RedactionResult(DecisionContent state, bool isDrop)
    {
        State = state;
        IsDrop = isDrop;
    }

    /// <summary>Gets the result that keeps no record of the decision. It is counted as a <c>dropped</c> record.</summary>
    public static RedactionResult Drop { get; } = new(default, isDrop: true);

    /// <summary>Gets the state to record; uninitialized for <see cref="Drop"/>.</summary>
    internal DecisionContent State { get; }

    /// <summary>Gets a value that says whether no record is kept.</summary>
    internal bool IsDrop { get; }

    /// <summary>Keeps a record of the decision with <paramref name="state"/> as its state: the state as it was, or rewritten.</summary>
    /// <param name="state">The state to record.</param>
    /// <returns>The result.</returns>
    /// <exception cref="ArgumentException"><paramref name="state"/> is uninitialized.</exception>
    public static RedactionResult Keep(DecisionContent state)
    {
        DecisionContent.EnsureInitialized(state, nameof(state));
        return new(state, isDrop: false);
    }
}
