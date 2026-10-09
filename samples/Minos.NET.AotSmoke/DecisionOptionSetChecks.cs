using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="DecisionOptionSet{T}"/> under Native AOT: a hand-written subclass, the shape the generator emits, constructed
/// and called through the base class, then used by <see cref="AnswerReader.ReadChoice{T}"/>.
/// </summary>
internal static class DecisionOptionSetChecks
{
    [Covers("Minos.DecisionOptionSet<T>.DecisionOptionSet() -> void")]
    [Covers("abstract Minos.DecisionOptionSet<T>.IndexOf(T value) -> int")]
    [Covers("abstract Minos.DecisionOptionSet<T>.IndexOfKey(ref System.Text.Json.Utf8JsonReader reader) -> int")]
    public static void HandWrittenOptionSetMapsOptions()
    {
        DecisionOptionSet<Team> options = new TeamOptionSet();
        var key = new Utf8JsonReader("\"account\""u8);
        key.Read();
        var unknownKey = new Utf8JsonReader("\"sales\""u8);
        unknownKey.Read();

        Program.Check(
            options.Count == 2
                && options.IndexOf(Team.Account) == 1
                && options.IndexOf((Team)42) == -1
                && options.IndexOfKey(ref key) == 1
                && options.IndexOfKey(ref unknownKey) == -1,
            "a DecisionOptionSet subclass maps values and keys to positions through the base class under Native AOT");

        var answer = new Utf8JsonReader(Encoding.UTF8.GetBytes("""{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7}"""));
        answer.Read();
        var choice = AnswerReader.ReadChoice(ref answer, options, new double[options.Count], 0);
        Program.Check(
            choice.Value == Team.Account && Math.Abs(choice.Probabilities[Team.Billing] - 0.2) < 1e-12,
            "AnswerReader.ReadChoice reads an answer through a hand-written option set under Native AOT");
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

        public override int IndexOfKey(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals("billing"u8))
            {
                return 0;
            }

            return reader.ValueTextEquals("account"u8) ? 1 : -1;
        }
    }
}
