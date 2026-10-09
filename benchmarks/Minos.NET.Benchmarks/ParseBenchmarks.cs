using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace Minos.Benchmarks;

public enum Team
{
    [Criteria("Charges, invoices, refunds")]
    Billing,

    [Criteria("Login, profile, permissions")]
    Account,
}

public enum Urgency
{
    [Level("Can wait")]
    Low,

    [Level("This week")]
    Medium,

    [Level("Today")]
    High,
}

/// <summary>A question set the generator turns into question JSON and a typed parser at compile time.</summary>
[Questions]
public partial record BenchTriage
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<Team> Team { get; }

    [Score("How urgent is `message`?")]
    public partial Score<Urgency> Urgency { get; }
}

/// <summary>A single-Noul question set whose wire key matches <c>NoulResponseJson</c>'s <c>is_urgent</c> answer,
/// so <see cref="ClientBenchmarks.TypedEvaluateNoulAsync"/> compares like for like against
/// <see cref="ClientBenchmarks.EvaluateAsync"/> over the same canned response.</summary>
[Questions]
public partial record BenchUrgency
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

/// <summary>Benchmarks the generated <c>BenchTriage.Parse</c> and the reader primitives it is built from, over the
/// same kind of fixed inputs as the Native AOT smoke app's allocation gates.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    private byte[] _triageAnswers = [];
    private byte[] _noulAnswer = [];
    private byte[] _choiceAnswer = [];
    private byte[] _scoreAnswer = [];
    private double[] _teamBuffer = [];
    private double[] _urgencyBuffer = [];

    [GlobalSetup]
    public void Setup()
    {
        _triageAnswers = Encoding.UTF8.GetBytes(
            """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""");
        _noulAnswer = Encoding.UTF8.GetBytes("""{"type":"noul","noul":0.95}""");
        _choiceAnswer = Encoding.UTF8.GetBytes(
            """{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7}""");
        _scoreAnswer = Encoding.UTF8.GetBytes(
            """{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}""");
        _teamBuffer = new double[TeamOptions.Instance.Count];
        _urgencyBuffer = new double[UrgencyOptions.Instance.Count];
    }

    /// <summary>The generated <c>BenchTriage.Parse</c>, over the whole triage answer set.</summary>
    [Benchmark]
    public BenchTriage GeneratedParse()
    {
        var reader = new Utf8JsonReader(_triageAnswers);
        reader.Read();
        return BenchTriage.Parse(ref reader);
    }

    /// <summary><see cref="AnswerReader.ReadNoul"/> over a fixed Noul answer.</summary>
    [Benchmark]
    public Noul ReadNoul()
    {
        var reader = new Utf8JsonReader(_noulAnswer);
        reader.Read();
        return AnswerReader.ReadNoul(ref reader);
    }

    /// <summary><see cref="AnswerReader.ReadChoice{T}"/> into a caller-owned buffer.</summary>
    [Benchmark]
    public Choice<Team> ReadChoice()
    {
        var reader = new Utf8JsonReader(_choiceAnswer);
        reader.Read();
        return AnswerReader.ReadChoice(ref reader, TeamOptions.Instance, _teamBuffer, 0);
    }

    /// <summary><see cref="AnswerReader.ReadScore{T}"/> into a caller-owned buffer.</summary>
    [Benchmark]
    public Score<Urgency> ReadScore()
    {
        var reader = new Utf8JsonReader(_scoreAnswer);
        reader.Read();
        return AnswerReader.ReadScore(ref reader, UrgencyOptions.Instance, _urgencyBuffer, 0);
    }

    /// <summary>Mirrors the generated option set for <see cref="Team"/>, since the real one is a private nested class.</summary>
    private sealed class TeamOptions : DecisionOptionSet<Team>
    {
        public static readonly TeamOptions Instance = new();

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

            if (reader.ValueTextEquals("account"u8))
            {
                return 1;
            }

            return -1;
        }
    }

    /// <summary>Mirrors the generated option set for <see cref="Urgency"/>, since the real one is a private nested class.</summary>
    private sealed class UrgencyOptions : DecisionOptionSet<Urgency>
    {
        public static readonly UrgencyOptions Instance = new();

        public override int Count => 3;

        public override Urgency this[int index] => index switch
        {
            0 => Urgency.Low,
            1 => Urgency.Medium,
            2 => Urgency.High,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Urgency value) => value switch
        {
            Urgency.Low => 0,
            Urgency.Medium => 1,
            Urgency.High => 2,
            _ => -1,
        };

        public override int IndexOfKey(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals("0"u8))
            {
                return 0;
            }

            if (reader.ValueTextEquals("1"u8))
            {
                return 1;
            }

            if (reader.ValueTextEquals("2"u8))
            {
                return 2;
            }

            return -1;
        }
    }
}
