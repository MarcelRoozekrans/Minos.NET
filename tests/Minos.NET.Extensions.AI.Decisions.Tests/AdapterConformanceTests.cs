using Minos.Conformance;
using Xunit.Abstractions;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// The Minos conformance suite, docs/conformance.md, run against both adapters: <see cref="UpstreamDecisionClient"/> over
/// an upstream fake that answers correctly, and a Minos fake taken to upstream and back, so every answer passes through
/// <see cref="MinosDecisionClient"/> too.
/// </summary>
/// <remarks>
/// An adapter sends nothing over HTTP, so the target uses the chat-completions wire, the one whose checks read the answers
/// as Minos read them: S3, D1 to D4 and M1 run on the adapter's result, and E1 checks that a request with no questions is
/// refused with Validation before anything is sent. E1 fails on both adapters, which refuse it with Unsupported; the test
/// pins that failure as a finding. The checks that read the wire, S1, S2, E2, E3, R1 and R3, have no exchange to read,
/// R2 has no model rule, since an adapter promises no model, and E4 meets no 429, so each of those is skipped with the
/// suite's own reason. The test pins every outcome and every skip reason, so a check that starts to skip, or to pass
/// without running, fails it.
/// </remarks>
public sealed class AdapterConformanceTests(ITestOutputHelper output)
{
    private const string Model = "model-x";

    public static TheoryData<string> Targets => ["over-upstream", "round-trip"];

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task TheConformanceSuite_RunsEveryCheckThatApplies(string name)
    {
        var sent = 0;
        var runner = new ConformanceRunner(() => new RefusingHandler(() => sent++), TimeProvider.System);
        var target = new ConformanceTarget(name, ConformanceWire.ChatCompletions, Model, modelRule: null, _ => Create(name));

        var report = await runner.RunAsync(target, ConformanceMode.Live);
        output.WriteLine(report.ToMarkdown());

        Assert.Equal(0, sent);
        Assert.All(report.Probes, probe => Assert.Null(probe.Skipped));
        var actual = report.Probes
            .SelectMany(probe => probe.Checks.Select(check => (probe.Probe, check.Id, check.Outcome, Detail: check.Outcome == CheckOutcome.Pass ? null : check.Detail)))
            .Concat(report.RunChecks.Select(check => ("run", check.Id, check.Outcome, check.Outcome == CheckOutcome.Pass ? null : (string?)check.Detail)))
            .ToArray();

        Assert.Equal(Expected(), actual);
    }

    private static UpstreamDecisionClient Create(string name) => name switch
    {
        "over-upstream" => new ScriptedUpstreamClient("fake-upstream", Model).AsMinosDecisionClient(),
        _ => new ScriptedMinosClient("fake-minos", Model).AsUpstreamDecisionClient().AsMinosDecisionClient(),
    };

    // A passing check is pinned by its outcome; a failing or skipped one by its detail too, so a skip reason cannot change unseen.
    private static (string Probe, string Id, CheckOutcome Outcome, string? Detail)[] Expected()
    {
        const string NoBody = "A chat completion carries no answer body; the answers are checked as Minos read them.";
        const string NoRule = "The target states no model rule.";
        var expected = new List<(string, string, CheckOutcome, string?)>();

        void Answer(string probe, bool choiceOrScore, bool score, bool mixed)
        {
            expected.Add((probe, "S1", CheckOutcome.Skipped, NoBody));
            expected.Add((probe, "S2", CheckOutcome.Skipped, NoBody));
            expected.Add((probe, "S3", CheckOutcome.Pass, null));
            expected.Add((probe, "D1", CheckOutcome.Pass, null));
            expected.Add(choiceOrScore ? (probe, "D2", CheckOutcome.Pass, null) : (probe, "D2", CheckOutcome.Skipped, "The probe asks no Choice or Score question."));
            expected.Add(score ? (probe, "D3", CheckOutcome.Pass, null) : (probe, "D3", CheckOutcome.Skipped, "The probe asks no Score question."));
            expected.Add(choiceOrScore ? (probe, "D4", CheckOutcome.Pass, null) : (probe, "D4", CheckOutcome.Skipped, "No answer reports a confidence."));
            if (mixed)
            {
                expected.Add((probe, "M1", CheckOutcome.Pass, null));
            }

            expected.Add((probe, "R1", CheckOutcome.Skipped, "No request was sent."));
            expected.Add((probe, "R2", CheckOutcome.Skipped, NoRule));
            expected.Add((probe, "R3", CheckOutcome.Skipped, "No request was sent."));
        }

        Answer("noul", choiceOrScore: false, score: false, mixed: false);
        Answer("choice", choiceOrScore: true, score: false, mixed: false);
        Answer("score", choiceOrScore: true, score: true, mixed: false);
        Answer("mixed", choiceOrScore: true, score: true, mixed: true);
        Answer("keyed", choiceOrScore: true, score: true, mixed: false);

        // FINDING, not a pass: the suite expects a request with no questions to be refused with Validation, as every other
        // provider does, and both adapters refuse it before sending with Unsupported, rule MIN207, as
        // docs/extensions-ai-decisions/mapping.md decides. The failure is pinned so it stays visible in the report and in
        // this test; when the adapter or the check changes, this line must change with it.
        expected.Add(("invalid", "E1", CheckOutcome.Fail, "Minos returned Unsupported, not Validation."));
        expected.Add(("invalid", "E3", CheckOutcome.Skipped, "No error response."));
        expected.Add(("bad-key", "E2", CheckOutcome.Skipped, "No request was sent."));
        expected.Add(("bad-key", "E3", CheckOutcome.Skipped, "No error response."));
        expected.Add(("run", "E4", CheckOutcome.Skipped, "No 429 was met during the run."));
        return [.. expected];
    }

    /// <summary>The runner's network: an adapter must never reach it, and a request that does fails.</summary>
    private sealed class RefusingHandler(Action onSend) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            onSend();
            throw new InvalidOperationException("An adapter sent an HTTP request.");
        }
    }
}
