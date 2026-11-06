using Minos.Evaluation;
using Minos.Samples.TicketTriage;
using Minos.SystemOne;
using ZeroAlloc.Results;

namespace Minos.Samples.Tests;

/// <summary>The triage command line with fake clients: both modes, the exit codes and the rule that no message text is printed.</summary>
[Collection("Environment")]
public sealed class TriageAppTests : IDisposable
{
    private const string Questions = """
        "questions":{"intent":{"type":"choice","instructions":"Which topic is this customer message about?","criteria":{"card_arrival":{"description":"Where a card is."},"lost_or_stolen_card":{"description":"A lost card."},"change_pin":{"description":"A new PIN."}}},"card_not_arrived":{"type":"noul","instructions":"Is the message about the arrival of a card?","criteria":{"true":"It is.","false":"It is not."}},"about_a_payment":{"type":"noul","instructions":"Is the message about a card payment?","criteria":{"true":"It is.","false":"It is not."}}}
        """;

    private const string Config = """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:1/","Model":"m"},"TypeSafe":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:2/","Model":"m"}}}}""";

    private const string Prices = """{"prices":[{"model":"m","inputPerMillion":1,"outputPerMillion":2,"date":"2026-01-01","source":"test"}]}""";

    private const string SecretText = "my card never showed up please help";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"triage-app-{Guid.NewGuid():N}");
    private readonly StringWriter _out = new();
    private readonly StringWriter _err = new();

    public TriageAppTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string Dataset(int count, string name = "d.jsonl")
        => Write(name, string.Join("\n", Enumerable.Range(1, count).Select(i =>
            $$"""{"id":"t-{{i}}","state":"{{SecretText}} {{i}}",{{Questions}},"expected":{"intent":"card_arrival","card_not_arrived":true,"about_a_payment":false},"source":"x#{{i}}","version":1}""")));

    private string[] RunArgs(string dataset, string? config = null, string? records = null)
        => ["--dataset", dataset, "--config", config ?? Write("minos.json", Config), "--records", records ?? Path.Combine(_dir, "records.jsonl")];

    private sealed class Fake(Func<DecisionRequest, Result<DecisionResponse, DecisionError>> answer) : IDecisionClient
    {
        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
            => new(answer(request));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static Result<DecisionResponse, DecisionError> Ok(DecisionRequest request, int intent, double confidence)
    {
        var answers = request.Definition.Questions.Select(q => q.Kind == QuestionKind.Noul
            ? QuestionAnswer.Noul(0.02)
            : QuestionAnswer.Choice(intent, confidence, Probabilities(q.Options.Count, intent, confidence))).ToArray();
        return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, answers, "m"));
    }

    private static double[] Probabilities(int count, int chosen, double confidence)
    {
        var p = new double[count];
        for (var i = 0; i < count; i++)
        {
            p[i] = i == chosen ? confidence : (1 - confidence) / (count - 1);
        }

        return p;
    }

    // The local client is unsure about the intent, so the target answers it.
    private static Func<string, SystemOneDecisionClientOptions, IDecisionClient> Working()
        => (section, _) => string.Equals(section, "Local", StringComparison.Ordinal)
            ? new Fake(r => Ok(r, 0, 0.5))
            : new Fake(r => Ok(r, 0, 0.99));

    private static Func<string, SystemOneDecisionClientOptions, IDecisionClient> Failing()
        => (_, _) => new Fake(_ => Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed")));

    private Task<int> RunAsync(string[] args, Func<string, SystemOneDecisionClientOptions, IDecisionClient>? factory = null, CancellationToken ct = default)
        => TriageApp.RunAsync(args, _out, _err, factory ?? Working(), ct);

    [Fact]
    public async Task Run_WithFakeClients_WritesRecordsAndThePrintedSummary()
    {
        var records = Path.Combine(_dir, "records.jsonl");

        var code = await RunAsync(RunArgs(Dataset(3), records: records));

        Assert.Equal(0, code);
        Assert.Equal(3, File.ReadAllLines(records).Length);
        Assert.Contains("items=3 decided=3 failed=0 escalated_questions=3 no_time_left=0 target_failed=0", _out.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_ThenReport_BuildsTheReportFromTheRecords()
    {
        var dataset = Dataset(4);
        var records = Path.Combine(_dir, "records.jsonl");
        var prices = Write("prices.json", Prices);
        var outDir = Path.Combine(_dir, "out");

        Assert.Equal(0, await RunAsync(RunArgs(dataset, records: records)));
        var code = await RunAsync(["--report", "--records", records, "--dataset", dataset, "--prices", prices, "--out", outDir]);

        Assert.Equal(0, code);
        var markdown = File.ReadAllText(Path.Combine(outDir, "triage-report.md"));
        Assert.Contains("Items read: 4. Decided: 4", markdown, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outDir, "triage-report.json")));
    }

    [Fact]
    public async Task MissingOption_ExitsTwo()
        => Assert.Equal(2, await RunAsync(["--dataset", Dataset(1), "--config", Write("minos.json", Config)]));

    [Fact]
    public async Task UnknownOption_ExitsTwo()
        => Assert.Equal(2, await RunAsync(["--bogus", "x"]));

    [Fact]
    public async Task RepeatedOption_ExitsTwo()
    {
        var dataset = Dataset(1);
        Assert.Equal(2, await RunAsync([.. RunArgs(dataset), "--dataset", dataset]));
    }

    [Fact]
    public async Task BadDatasetLine_ExitsTwo()
        => Assert.Equal(2, await RunAsync(RunArgs(Write("bad.jsonl", "not json"))));

    [Fact]
    public async Task UnknownSection_ExitsTwo()
    {
        var noSection = Write("other.json", """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:1/","Model":"m"}}}}""");

        Assert.Equal(2, await RunAsync(RunArgs(Dataset(1), noSection)));
        Assert.Contains("TypeSafe", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OversizeDataset_ExitsTwo()
        => Assert.Equal(2, await RunAsync(RunArgs(Dataset(TriageApp.MaxItems + 1, "big.jsonl"))));

    [Fact]
    public async Task NoneDecided_ExitsOne()
        => Assert.Equal(1, await RunAsync(RunArgs(Dataset(1)), Failing()));

    [Fact]
    public async Task Cancelled_ExitsOneThirty()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal(130, await RunAsync(RunArgs(Dataset(1)), ct: cancelled.Token));
    }

    private string[] ReportArgs(string records, string dataset)
        => ["--records", records, "--dataset", dataset, "--prices", Write("prices.json", Prices), "--out", Path.Combine(_dir, "out")];

    [Fact]
    public async Task ReportMode_RejectsConfig()
    {
        var records = Path.Combine(_dir, "records.jsonl");
        var dataset = Dataset(1);
        Assert.Equal(0, await RunAsync(RunArgs(dataset, records: records)));

        Assert.Equal(2, await RunAsync(["--report", .. ReportArgs(records, dataset), "--config", Write("minos.json", Config)]));
    }

    [Fact]
    public async Task RunMode_RejectsOutAndPrices()
    {
        var dataset = Dataset(1);

        Assert.Equal(2, await RunAsync([.. RunArgs(dataset), "--out", Path.Combine(_dir, "out")]));
        Assert.Equal(2, await RunAsync([.. RunArgs(dataset), "--prices", Write("prices.json", Prices)]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task ReportFlag_DoesNotConsumeItsNeighbour_WhateverItsPosition(int position)
    {
        var records = Path.Combine(_dir, "records.jsonl");
        var dataset = Dataset(2);
        Assert.Equal(0, await RunAsync(RunArgs(dataset, records: records)));
        var args = ReportArgs(records, dataset).ToList();
        args.Insert(position, "--report");

        Assert.Equal(0, await RunAsync([.. args]));
        Assert.Contains("Items read: 2.", File.ReadAllText(Path.Combine(_dir, "out", "triage-report.md")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyOut_ExitsTwo_AndNamesTheOption()
    {
        var records = Path.Combine(_dir, "records.jsonl");
        var dataset = Dataset(1);
        Assert.Equal(0, await RunAsync(RunArgs(dataset, records: records)));

        Assert.Equal(2, await RunAsync(["--report", "--records", records, "--dataset", dataset, "--prices", Write("prices.json", Prices), "--out", ""]));
        Assert.Contains("--out", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyRecords_ExitsTwo_AndNamesTheOption()
    {
        Assert.Equal(2, await RunAsync(RunArgs(Dataset(1), records: "")));
        Assert.Contains("--records", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValueThatLooksLikeAnOption_ExitsTwo()
    {
        Assert.Equal(2, await RunAsync(["--dataset", "--config", Write("minos.json", Config), "--records", Path.Combine(_dir, "r.jsonl")]));
        Assert.Contains("--dataset", _err.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Report_ExitsTwo_ForAnUnknownRecordId()
    {
        var records = Path.Combine(_dir, "records.jsonl");
        Assert.Equal(0, await RunAsync(RunArgs(Dataset(2), records: records)));
        var smaller = Dataset(1, "small.jsonl");

        var code = await RunAsync(["--report", "--records", records, "--dataset", smaller, "--prices", Write("prices.json", Prices), "--out", Path.Combine(_dir, "out")]);

        Assert.Equal(2, code);
        Assert.Contains("t-2", _err.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(SecretText, _out.ToString() + _err, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Run_DeletesAnOldRecordsFile()
    {
        var records = Write("records.jsonl", "earlier line\n");

        Assert.Equal(0, await RunAsync(RunArgs(Dataset(1), records: records)));

        Assert.DoesNotContain("earlier", File.ReadAllText(records), StringComparison.Ordinal);
        Assert.Single(File.ReadAllLines(records));
    }

    [Fact]
    public async Task Output_HasNoMessageText()
    {
        var dataset = Dataset(3);
        var records = Path.Combine(_dir, "records.jsonl");
        await RunAsync(RunArgs(dataset, records: records));
        await RunAsync(RunArgs(dataset, records: records), Failing());
        await RunAsync(["--report", "--records", records, "--dataset", dataset, "--prices", Write("prices.json", Prices), "--out", Path.Combine(_dir, "out")]);

        Assert.DoesNotContain(SecretText, _out.ToString() + _err, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretText, File.ReadAllText(records), StringComparison.Ordinal);
    }
}
