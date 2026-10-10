namespace Minos.AotSmoke;

/// <summary>
/// <see cref="DecisionOptionSet{T}"/> under Native AOT: a hand-written subclass, the shape the generator emits, constructed
/// and called through the base class. The slot probe in <see cref="AllocationChecks"/> reads answers through hand-written
/// sets of the same shape with <see cref="AnswerSlots.Choice{T}"/> and <see cref="AnswerSlots.Score{T}"/>.
/// </summary>
internal static class DecisionOptionSetChecks
{
    [Covers("Minos.DecisionOptionSet<T>.DecisionOptionSet() -> void")]
    [Covers("abstract Minos.DecisionOptionSet<T>.IndexOf(T value) -> int")]
    public static void HandWrittenOptionSetMapsOptions()
    {
        DecisionOptionSet<Team> options = new TeamOptionSet();

        Program.Check(
            options.Count == 2
                && options[1] == Team.Account
                && options.IndexOf(Team.Account) == 1
                && options.IndexOf((Team)42) == -1,
            "a DecisionOptionSet subclass maps values to positions through the base class under Native AOT");
    }

    private sealed class TeamOptionSet : DecisionOptionSet<Team>
    {
        public override int Count => 2;

        public override Team this[int index] => index switch
        {
            0 => Team.Billing,
            1 => Team.Account,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Team value) => value switch
        {
            Team.Billing => 0,
            Team.Account => 1,
            _ => -1,
        };
    }
}
