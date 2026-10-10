using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Minos.Protocols;

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

/// <summary>Benchmarks the systemone protocol's read of <c>BenchTriage</c>'s answers and the reader primitives it is
/// built from, over the same kind of fixed inputs as the Native AOT smoke app's allocation gates.</summary>
[MemoryDiagnoser]
public class ParseBenchmarks
{
    // One cached factory, as the typed client path keeps, so the benchmark measures the read and not a delegate allocation.
    private static readonly AnswerFactory<BenchTriage> TriageFactory = static answers => BenchTriage.Create(answers);

    private static readonly byte[][] TeamKeys = Utf8Keys.Encode(["billing", "account"]);
    private static readonly byte[][] UrgencyKeys = Utf8Keys.Encode(["0", "1", "2"]);

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
        _teamBuffer = new double[TeamKeys.Length];
        _urgencyBuffer = new double[UrgencyKeys.Length];
    }

    /// <summary>The protocol's read of the whole triage answer set into <c>BenchTriage</c>.</summary>
    [Benchmark]
    public BenchTriage ProtocolReadAnswers()
    {
        var reader = new Utf8JsonReader(_triageAnswers);
        reader.Read();
        return SystemOneProtocol.Instance.ReadAnswers(ref reader, BenchTriage.Definition, TriageFactory);
    }

    /// <summary><see cref="SystemOneAnswers.ReadNoul"/> over a fixed Noul answer.</summary>
    [Benchmark]
    public double ReadNoul()
    {
        var reader = new Utf8JsonReader(_noulAnswer);
        reader.Read();
        return SystemOneAnswers.ReadNoul(ref reader);
    }

    /// <summary><see cref="SystemOneAnswers.ReadChoice"/> into a caller-owned buffer.</summary>
    [Benchmark]
    public (int Choice, double Confidence) ReadChoice()
    {
        var reader = new Utf8JsonReader(_choiceAnswer);
        reader.Read();
        return SystemOneAnswers.ReadChoice(ref reader, TeamKeys, _teamBuffer, 0);
    }

    /// <summary><see cref="SystemOneAnswers.ReadScore"/> into a caller-owned buffer.</summary>
    [Benchmark]
    public (int Level, double Expected, double Confidence) ReadScore()
    {
        var reader = new Utf8JsonReader(_scoreAnswer);
        reader.Read();
        return SystemOneAnswers.ReadScore(ref reader, UrgencyKeys, _urgencyBuffer, 0);
    }
}
