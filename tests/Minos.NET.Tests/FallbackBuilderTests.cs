using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Minos.Tests;

/// <summary>The core <c>UseFallback</c> builder extensions.</summary>
public sealed class FallbackBuilderTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static readonly Dictionary<string, QuestionAnswer> StepAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.85, [0.05, 0.85, 0.1]),
    };

    [Fact]
    public async Task UseFallback_builds_the_stage()
    {
        var clef = StubDecisionClient.Answering(StepAnswers, "clef-model");
        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", clef)))
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.NotNull(client.GetService<FallbackDecisionClient>());
        Assert.Equal("clef-model", response.Model);
        Assert.All(response.Path!.Routes, route => Assert.Equal(new { AnsweredBy = "clef", Reason = RouteReason.FellBack }, new { route.AnsweredBy, route.Reason }));
    }

    [Fact]
    public async Task UseFallback_with_a_factory_creates_the_options_from_the_builds_services()
    {
        var clef = StubDecisionClient.Answering(StepAnswers);
        var services = new Services { [typeof(IDecisionClient)] = clef };
        IServiceProvider? seen = null;

        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(s =>
            {
                seen = s;
                var options = new FallbackOptions { PrimaryName = "local" };
                options.Steps.Add(new FallbackStep("clef", (IDecisionClient)s.GetService(typeof(IDecisionClient))!));
                return options;
            })
            .Build(services);

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.Same(services, seen);
        Assert.Equal("local", response.Path!.Calls[0].ClientName);
        Assert.Equal(1, clef.Calls);
    }

    [Fact]
    public void UseFallback_takes_the_logger_and_TimeProvider_from_the_services()
    {
        var time = new FakeTimeProvider();
        var services = new Services { [typeof(ILoggerFactory)] = NullLoggerFactory.Instance, [typeof(TimeProvider)] = time };

        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers))))
            .Build(services);

        var stage = client.GetService<FallbackDecisionClient>()!;
        Assert.Same(time, stage.TimeProvider);
        Assert.NotNull(stage.Logger);
    }

    [Fact]
    public void Without_services_the_stage_reads_the_system_clock_and_logs_nowhere()
    {
        using var client = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers))))
            .Build();

        var stage = client.GetService<FallbackDecisionClient>()!;
        Assert.Same(TimeProvider.System, stage.TimeProvider);
        Assert.Null(stage.Logger);
    }

    [Fact]
    public void A_second_UseFallback_throws_at_Build()
    {
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers))))
            .UseRetries()
            .UseFallback(o => o.Steps.Add(new FallbackStep("hosted", StubDecisionClient.Answering(StepAnswers))));

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("one fallback stage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_options_throw_at_Build()
    {
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(_ => { });

        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.Build());

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("Steps", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_options_from_a_factory_throw_at_Build()
    {
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(_ => new FallbackOptions());

        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.Build());

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void A_factory_returning_null_throws_at_Build()
    {
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(_ => null!);

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public async Task UseRetries_inside_retries_step_zero_before_falling_back()
    {
        var primary = StubDecisionClient.Failing(DecisionErrorKind.Server);
        var clef = StubDecisionClient.Answering(StepAnswers);
        var order = new List<string>();
        var recordingPrimary = new StubDecisionClient(async (request, token) =>
        {
            order.Add("primary");
            return await primary.EvaluateAsync(request, token);
        });
        var recordingClef = new StubDecisionClient(async (request, token) =>
        {
            order.Add("clef");
            return await clef.EvaluateAsync(request, token);
        });
        using var client = new DecisionClientBuilder(recordingPrimary)
            .UseFallback(o => o.Steps.Add(new FallbackStep("clef", recordingClef)))
            .UseRetries(o =>
            {
                o.MaxRetries = 2;
                o.InitialBackoff = TimeSpan.FromMilliseconds(1);
                o.MaxRetryDelay = TimeSpan.FromMilliseconds(1);
                o.Jitter = false;
            })
            .Build();

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State));

        Assert.True(result.IsSuccess);
        Assert.Equal(["primary", "primary", "primary", "clef"], order);
        Assert.Equal([0, 1, 2], primary.Requests.Select(r => r.RetryAttempt));
        Assert.Equal(0, Assert.Single(clef.Requests).RetryAttempt);
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server));
        Action<FallbackOptions> configure = _ => { };
        Func<IServiceProvider, FallbackOptions> factory = _ => new FallbackOptions();

        Assert.Equal("builder", Assert.Throws<ArgumentNullException>(() => ((DecisionClientBuilder)null!).UseFallback(configure)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.UseFallback((Action<FallbackOptions>)null!)).ParamName);
        Assert.Equal("builder", Assert.Throws<ArgumentNullException>(() => ((DecisionClientBuilder)null!).UseFallback(factory)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.UseFallback((Func<IServiceProvider, FallbackOptions>)null!)).ParamName);
    }

    [Fact]
    public void Each_Build_configures_new_options()
    {
        var calls = 0;
        var builder = new DecisionClientBuilder(StubDecisionClient.Failing(DecisionErrorKind.Server))
            .UseFallback(o =>
            {
                calls++;
                o.Steps.Add(new FallbackStep("clef", StubDecisionClient.Answering(StepAnswers)));
            });

        using var first = builder.Build();
        using var second = builder.Build();

        Assert.Equal(2, calls);
    }

    private sealed class Services : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
