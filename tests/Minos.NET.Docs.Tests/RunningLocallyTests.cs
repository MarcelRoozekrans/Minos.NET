using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.Recording;

namespace Minos.Docs.Tests;

/// <summary>
/// The running-locally guide's example app runs on the two configurations the page shows, with every HTTP reply
/// scripted, and does what the page says it does.
/// </summary>
public sealed class RunningLocallyTests : IDisposable
{
    private const string TypeSafeResponse = """
        {
          "model": "jev-1.14.0",
          "answers": {
            "is_disputed": { "type": "noul", "noul": 0.91 },
            "topic": { "type": "choice", "choice": "duplicate", "probabilities": { "refund": 0.1, "fraud": 0.05, "duplicate": 0.85 }, "confidence": 0.8 }
          },
          "usage": { "input_tokens": 60, "output_tokens": 4 }
        }
        """;

    private const string LocalResponse = """
        {
          "model": "Mapika/decider-2b-GGUF:Q4_K_M",
          "answers": {
            "is_disputed": { "type": "noul", "noul": 0.27 },
            "topic": { "type": "choice", "choice": "refund", "probabilities": { "refund": 0.6, "fraud": 0.1, "duplicate": 0.3 }, "confidence": 0.5 }
          }
        }
        """;

    private const string ChargedTwice = "Charged twice for one order, please fix it.";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("minos-docs-running-locally-");

    private string RecordFile => Path.Combine(_directory.FullName, "decisions.jsonl");

    [Fact]
    public async Task BeforeTheSwitch_TypeSafeDecides_AndTheLocalAnswersAreRecordedBeside()
    {
        PaymentReview? review = null;
        var shadowed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (typeSafe, local) = await RunAsync(RunningLocallyConfig.Before, Reply.Ok(TypeSafeResponse), shadowed, async services =>
        {
            (review, _) = await PaymentReviews.ReviewAsync(services.GetRequiredService<IDecisionClient>(), "customer-7", ChargedTwice, CancellationToken.None);

            // The candidate answers in the background; the test waits for its record before the host stops.
            await shadowed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        });

        // The result is the primary's.
        Assert.NotNull(review);
        Assert.Equal(PaymentTopic.Duplicate, review.Topic.Value);
        Assert.Equal(0.91, review.IsDisputed.Probability, 3);

        var sent = Assert.Single(typeSafe.Requests);
        Assert.Equal(new Uri("https://api.typesafe.ai/v1/systemone"), sent.Uri);
        var shadow = Assert.Single(local.Requests);
        Assert.Equal(new Uri("http://localhost:8000/v1/systemone"), shadow.Uri);
        Assert.Null(shadow.Authorization);

        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal("customer-7", decision.Record.ErasureKey);
        Assert.Equal("jev-1.14.0", decision.Record.ServedModel);
        Assert.Equal(["is_disputed", "topic"], decision.Record.Answers.Select(answer => answer.Key));
        Assert.Equal(0.91, decision.Record.Answers[0].Probability, 3);
        var candidate = decision.Record.Shadow;
        Assert.NotNull(candidate);
        Assert.Null(candidate.ErrorKind);
        Assert.Equal("Mapika/decider-2b-GGUF:Q4_K_M", candidate.ServedModel);
        Assert.Equal(["is_disputed", "topic"], candidate.Answers.Select(answer => answer.Key));
        Assert.Equal(0.27, candidate.Answers[0].Probability, 3);
    }

    [Fact]
    public async Task AfterTheSwitch_TheLocalServerDecides_AndNothingIsShadowed()
    {
        PaymentReview? review = null;
        var (typeSafe, local) = await RunAsync(RunningLocallyConfig.After, Reply.Ok(LocalResponse), shadowed: null, async services =>
            (review, _) = await PaymentReviews.ReviewAsync(services.GetRequiredService<IDecisionClient>(), "customer-7", ChargedTwice, CancellationToken.None));

        // The active client now answers from the local server, through the registration TypeSafe had.
        Assert.NotNull(review);
        Assert.Equal(PaymentTopic.Refund, review.Topic.Value);
        Assert.Empty(local.Requests);
        var sent = Assert.Single(typeSafe.Requests);
        Assert.Equal(new Uri("http://localhost:8000/v1/systemone"), sent.Uri);
        Assert.Null(sent.Authorization);

        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal("Mapika/decider-2b-GGUF:Q4_K_M", decision.Record.ServedModel);
        Assert.Null(decision.Record.Shadow);
    }

    [Fact]
    public async Task AShadowNamingTheActiveSection_FailsNamingIt()
    {
        var builder = NewBuilder(RunningLocallyConfig.After, new(StringComparer.Ordinal) { ["Minos:Shadow"] = "Local" });
        await using var sink = new JsonlDecisionRecordSink(RecordFile);

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddRecordedDecisions(sink));

        Assert.Contains("Local", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AShadowNamingNoSection_FailsNamingIt()
    {
        var builder = NewBuilder(RunningLocallyConfig.Before, new(StringComparer.Ordinal) { ["Minos:Shadow"] = "Elsewhere" });
        await using var sink = new JsonlDecisionRecordSink(RecordFile);

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddRecordedDecisions(sink));

        Assert.Contains("Elsewhere", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailedPrimary_IsTheCallersError_WhileShadowing()
    {
        PaymentReview? review = null;
        string? decisionId = null;
        var (typeSafe, local) = await RunAsync(
            RunningLocallyConfig.Before,
            Reply.Error(500, """{"error":{"message":"upstream failed"}}"""),
            shadowed: null,
            async services => (review, decisionId) = await PaymentReviews.ReviewAsync(
                services.GetRequiredService<IDecisionClient>(), "customer-7", ChargedTwice, CancellationToken.None),
            overrides: new(StringComparer.Ordinal) { ["Minos:Providers:TypeSafe:MaxRetries"] = "0" });

        // The caller gets the primary's failure as a result, not an exception, and the local answer never stands in for
        // it: a failed decision gives nothing to compare, so it is recorded and not shadowed.
        Assert.Null(review);
        Assert.NotNull(decisionId);
        Assert.Single(typeSafe.Requests);
        Assert.Empty(local.Requests);
        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal(decisionId, decision.Record.DecisionId);
        Assert.NotNull(decision.Record.ErrorKind);
        Assert.Empty(decision.Record.Answers);
        Assert.Null(decision.Record.Shadow);
    }

    [Fact]
    public async Task TheRecord_KeepsTheTextWithAddressesMasked()
    {
        const string text = "Charged twice, write to ann@example.com";
        var (typeSafe, _) = await RunAsync(RunningLocallyConfig.After, Reply.Ok(LocalResponse), shadowed: null, async services =>
            await PaymentReviews.ReviewAsync(services.GetRequiredService<IDecisionClient>(), "customer-7", text, CancellationToken.None));

        // The provider is sent the text unchanged; only the kept record is masked.
        Assert.Contains("ann@example.com", Assert.Single(typeSafe.Requests).Body, StringComparison.Ordinal);
        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.True(decision.Record.State.TryGetString(out var kept));
        Assert.DoesNotContain("ann@example.com", kept, StringComparison.Ordinal);
        Assert.Equal("Charged twice, write to [email]", kept);
        Assert.DoesNotContain("ann@example.com", await File.ReadAllTextAsync(RecordFile), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PaymentTopic.Refund, "refund")]
    [InlineData(PaymentTopic.Fraud, "fraud")]
    [InlineData(PaymentTopic.Duplicate, "duplicate")]
    public async Task AConfirmedOutcome_LandsOnItsDecision(PaymentTopic topic, string key)
    {
        string? decisionId = null;
        await RunAsync(RunningLocallyConfig.After, Reply.Ok(LocalResponse), shadowed: null, async services =>
        {
            (_, decisionId) = await PaymentReviews.ReviewAsync(services.GetRequiredService<IDecisionClient>(), "customer-7", ChargedTwice, CancellationToken.None);
            await PaymentOutcomes.ConfirmAsync(services.GetRequiredService<IDecisionRecorder>(), decisionId!, true, topic, CancellationToken.None);
        });

        var decision = Assert.Single(await RecordFileReading.ReadAsync(RecordFile, CancellationToken.None));
        Assert.Equal(decisionId, decision.Record.DecisionId);

        // The key the outcome confirms is one the question set sends.
        var topicQuestion = decision.Record.Definition.Questions.Single(question => string.Equals(question.Key, "topic", StringComparison.Ordinal));
        Assert.Contains(key, topicQuestion.Options.Select(option => option.Key));
        var confirmed = Assert.IsType<ConfirmedOutcome>(Assert.Single(decision.Outcomes));
        Assert.Equal("payments-desk", confirmed.Source);
        Assert.Equal(ConfirmedAnswer.ForNoul(true), confirmed.Answers["is_disputed"]);
        Assert.Equal(ConfirmedAnswer.ForChoice(key), confirmed.Answers["topic"]);
    }

    public void Dispose() => _directory.Delete(recursive: true);

    // Builds the app's host on the configuration with every HTTP reply scripted, runs the work, then stops the host
    // and closes the sink, so the file holds everything. The default registration is the active client; the keyed
    // Local one is the shadow candidate.
    private async Task<(ScriptedDecision.Handler Active, ScriptedDecision.Handler Candidate)> RunAsync(
        string configuration,
        Reply activeReply,
        TaskCompletionSource? shadowed,
        Func<IServiceProvider, Task> work,
        Dictionary<string, string?>? overrides = null)
    {
        var active = new ScriptedDecision.Handler([activeReply]);
        var candidate = new ScriptedDecision.Handler([Reply.Ok(LocalResponse)]);
        var builder = NewBuilder(configuration, overrides);
        var sink = new SignallingSink(new JsonlDecisionRecordSink(RecordFile), shadowed);
        try
        {
            builder.AddRecordedDecisions(sink);
            builder.Services.AddHttpClient("Minos.NET").ConfigurePrimaryHttpMessageHandler(() => active);
            builder.Services.AddHttpClient("Minos.NET:Local").ConfigurePrimaryHttpMessageHandler(() => candidate);
            var host = builder.Build();
            try
            {
                await host.StartAsync();
                await work(host.Services);
                await host.StopAsync();
            }
            finally
            {
                await ((IAsyncDisposable)host).DisposeAsync();
            }
        }
        finally
        {
            await sink.DisposeAsync();
        }

        return (active, candidate);
    }

    // No file and no reload: the configuration comes from the page's JSON text, plus a placeholder TypeSafe key.
    private static HostApplicationBuilder NewBuilder(string configuration, Dictionary<string, string?>? overrides = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(configuration)));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal) { ["Minos:Providers:TypeSafe:ApiKey"] = "docs-key" });
        if (overrides is not null)
        {
            builder.Configuration.AddInMemoryCollection(overrides);
        }

        return builder;
    }

    // Forwards to the JSONL sink and reports when a record arrives with its shadow part.
    private sealed class SignallingSink(JsonlDecisionRecordSink inner, TaskCompletionSource? shadowed) : IDecisionRecordSink, IAsyncDisposable
    {
        public async ValueTask WriteAsync(DecisionRecord record, CancellationToken cancellationToken)
        {
            await inner.WriteAsync(record, cancellationToken);
            if (record.Shadow is not null)
            {
                shadowed?.TrySetResult();
            }
        }

        public ValueTask AddOutcomeAsync(string decisionId, DecisionOutcome outcome, CancellationToken cancellationToken)
            => inner.AddOutcomeAsync(decisionId, outcome, cancellationToken);

        public ValueTask EraseAsync(string erasureKey, CancellationToken cancellationToken) => inner.EraseAsync(erasureKey, cancellationToken);

        public ValueTask EraseDecisionAsync(string decisionId, CancellationToken cancellationToken) => inner.EraseDecisionAsync(decisionId, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
