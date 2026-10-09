using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using ZeroAlloc.Results;

namespace Minos.Benchmarks;

/// <summary>Twenty Noul questions, the generated counterpart of <see cref="QuestionSetBenchmarks"/>' built twenty-question set.</summary>
[JevQuestions]
public partial record BenchTwenty
{
    [Noul("Question 1?")] public partial Noul Q01 { get; }
    [Noul("Question 2?")] public partial Noul Q02 { get; }
    [Noul("Question 3?")] public partial Noul Q03 { get; }
    [Noul("Question 4?")] public partial Noul Q04 { get; }
    [Noul("Question 5?")] public partial Noul Q05 { get; }
    [Noul("Question 6?")] public partial Noul Q06 { get; }
    [Noul("Question 7?")] public partial Noul Q07 { get; }
    [Noul("Question 8?")] public partial Noul Q08 { get; }
    [Noul("Question 9?")] public partial Noul Q09 { get; }
    [Noul("Question 10?")] public partial Noul Q10 { get; }
    [Noul("Question 11?")] public partial Noul Q11 { get; }
    [Noul("Question 12?")] public partial Noul Q12 { get; }
    [Noul("Question 13?")] public partial Noul Q13 { get; }
    [Noul("Question 14?")] public partial Noul Q14 { get; }
    [Noul("Question 15?")] public partial Noul Q15 { get; }
    [Noul("Question 16?")] public partial Noul Q16 { get; }
    [Noul("Question 17?")] public partial Noul Q17 { get; }
    [Noul("Question 18?")] public partial Noul Q18 { get; }
    [Noul("Question 19?")] public partial Noul Q19 { get; }
    [Noul("Question 20?")] public partial Noul Q20 { get; }
}

/// <summary>Question sets built at run time: building one, evaluating one, and parsing twenty answers against the generated parser.</summary>
[MemoryDiagnoser]
public class QuestionSetBenchmarks
{
    private const string State = "Help! My payouts have been failing for 3 days.";

    private JevQuestionSetBuilder _builder = null!;
    private JevQuestionSet _triage = null!;
    private JevQuestionSet _twenty = null!;
    private byte[] _twentyAnswers = [];
    private HttpClient _http = null!;
    private JevClient _client = null!;

    /// <summary>The Noul, enum Choice and enum Score of <see cref="ClientBenchmarks.TypedEvaluateAsync"/>, as a builder.</summary>
    internal static JevQuestionSetBuilder TriageBuilder()
        => JevQuestionSet.CreateBuilder()
            .Noul("requests_credentials", "Does `message` ask for a credential?", out _)
            .Choice<Team>("team", "Which team should handle `message`?", out _, o => o
                .Describe(Team.Billing, "Charges, invoices, refunds")
                .Describe(Team.Account, "Login, profile, permissions"))
            .Score<Urgency>("urgency", "How urgent is `message`?", out _, l => l
                .Level(Urgency.Low, "Can wait").Level(Urgency.Medium, "This week").Level(Urgency.High, "Today"));

    [GlobalSetup]
    public void Setup()
    {
        _builder = TriageBuilder();
        _triage = _builder.Build().Value;

        var twenty = JevQuestionSet.CreateBuilder();
        var answers = new StringBuilder("{");
        for (var i = 1; i <= 20; i++)
        {
            var key = "q" + i.ToString("00", CultureInfo.InvariantCulture);
            twenty.Noul(key, "Question " + i.ToString(CultureInfo.InvariantCulture) + "?", out _);
            answers.Append(i > 1 ? "," : string.Empty).Append('"').Append(key).Append("\":{\"type\":\"noul\",\"noul\":0.5}");
        }

        _twenty = twenty.Build().Value;
        _twentyAnswers = Encoding.UTF8.GetBytes(answers.Append('}').ToString());

        (_http, _client) = ClientBenchmarks.CreateClient(ClientBenchmarks.TriageResponseJson);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _client.Dispose();
        _http.Dispose();
    }

    /// <summary><see cref="JevQuestionSetBuilder.Build"/> of a Noul, an enum Choice and an enum Score.</summary>
    [Benchmark]
    public Result<JevQuestionSet, JevError> Build() => _builder.Build();

    /// <summary>The same three questions as <see cref="ClientBenchmarks.TypedEvaluateAsync"/>, evaluated as a built set.</summary>
    [Benchmark]
    public ValueTask<Result<JevAnswers, JevError>> EvaluateBuiltSet() => _client.EvaluateAsync(_triage, State);

    /// <summary>Parses twenty answers by the built set's linear key scan.</summary>
    [Benchmark]
    public JevAnswers ParseBuiltTwenty()
    {
        var reader = new Utf8JsonReader(_twentyAnswers);
        reader.Read();
        return _twenty.Parse(ref reader);
    }

    /// <summary>Parses the same twenty answers with the generated parser, as the baseline for the key scan.</summary>
    [Benchmark]
    public BenchTwenty ParseGeneratedTwenty()
    {
        var reader = new Utf8JsonReader(_twentyAnswers);
        reader.Read();
        return BenchTwenty.Parse(ref reader);
    }
}
