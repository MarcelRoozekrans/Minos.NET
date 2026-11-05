using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Results;

namespace Minos.DependencyInjection.Tests;

/// <summary>The by-key <c>UseEscalation</c>, which re-asks on a keyed client registered with <c>AddDecisionClient</c>.</summary>
public sealed class EscalationRegistrationTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static QuestionSetDefinition Triage() => new(
        QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")),
        QuestionDefinition.Choice(
            "department",
            DecisionContent.FromString("Which team should handle this?"),
            new OptionDefinition("billing", Criterion.Text("Payments")),
            new OptionDefinition("tech", Criterion.Text("Bugs")),
            new OptionDefinition("other", null)));

    // department at 0.55 is unsure by the default threshold of 0.6.
    private static readonly Dictionary<string, QuestionAnswer> UnsurePrimary = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
    };

    [Fact]
    public async Task UseEscalation_by_key_resolves_the_keyed_client()
    {
        var hosted = new AnsweringClient(TargetAnswers);
        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => hosted);
        services.AddDecisionClient(_ => new AnsweringClient(UnsurePrimary)).UseEscalation("hosted");
        using var provider = services.BuildServiceProvider();

        var response = (await provider.GetRequiredService<IDecisionClient>().EvaluateAsync(new DecisionRequest(Triage(), State))).Value;

        Assert.Equal(1, hosted.Calls);
        Assert.Equal(RouteReason.Escalated, response.Path!.Routes[1].Reason);
        Assert.Equal(1, response.Answers[1].ChosenIndex);
    }

    [Fact]
    public async Task The_target_name_is_the_key()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => new AnsweringClient(TargetAnswers));
        services.AddDecisionClient(_ => new AnsweringClient(UnsurePrimary))
            .UseEscalation("hosted", o =>
            {
                o.TargetName = "another name";
                o.PrimaryName = "local";
            });
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage(), State))).Value;

        Assert.Equal("hosted", client.GetService<EscalationOptions>()!.TargetName);
        Assert.Equal(["local", "hosted"], response.Path!.Calls.Select(c => c.ClientName));
        Assert.Equal("hosted", response.Path.Routes[1].AnsweredBy);
    }

    [Fact]
    public void A_missing_key_throws_naming_it()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(_ => new AnsweringClient(UnsurePrimary)).UseEscalation("hosted");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("'hosted'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_key_throws_naming_it_without_a_container()
    {
        var builder = new DecisionClientBuilder(new AnsweringClient(UnsurePrimary)).UseEscalation("hosted");

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("'hosted'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_UseEscalation_throws_at_Build()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("clef", _ => new AnsweringClient(TargetAnswers));
        services.AddDecisionClient("hosted", _ => new AnsweringClient(TargetAnswers));
        services.AddDecisionClient(_ => new AnsweringClient(UnsurePrimary)).UseEscalation("hosted").UseEscalation("clef");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("a pipeline holds one escalation stage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_cycle_between_keyed_clients_throws_naming_the_key()
    {
        // Each Build calls its client factory first, so the counter stops a broken guard with an exception long before the
        // stack overflows: the test then fails cleanly instead of taking down the test host.
        var builds = 0;
        IDecisionClient Bounded(IReadOnlyDictionary<string, QuestionAnswer> answers)
            => Interlocked.Increment(ref builds) <= 20
                ? new AnsweringClient(answers)
                : throw new InvalidOperationException("Recursion not stopped by the cycle guard.");
        var services = new ServiceCollection();
        services.AddDecisionClient("a", _ => Bounded(UnsurePrimary)).UseEscalation("b");
        services.AddDecisionClient("b", _ => Bounded(UnsurePrimary)).UseEscalation("a");
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("a"));

        Assert.Contains("found a cycle: the decision client 'b' escalates back to itself", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_shared_by_two_clients_is_not_a_cycle()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => new AnsweringClient(TargetAnswers));
        services.AddDecisionClient("a", _ => new AnsweringClient(UnsurePrimary)).UseEscalation("hosted");
        services.AddDecisionClient(_ => new AnsweringClient(UnsurePrimary)).UseEscalation("a");
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IDecisionClient>());
        Assert.NotNull(provider.GetRequiredKeyedService<IDecisionClient>("hosted"));
    }

    [Fact]
    public void A_client_escalating_to_itself_throws()
    {
        var services = new ServiceCollection();
        var builder = services.AddDecisionClient("hosted", _ => new AnsweringClient(UnsurePrimary));

        var exception = Assert.Throws<ArgumentException>(() => builder.UseEscalation("hosted"));

        Assert.Equal("targetKey", exception.ParamName);
    }

    [Fact]
    public void Invalid_arguments_throw()
    {
        var builder = new DecisionClientBuilder(new AnsweringClient(UnsurePrimary));

        Assert.Equal("builder", Assert.Throws<ArgumentNullException>(() => ((DecisionClientBuilder)null!).UseEscalation("hosted")).ParamName);
        Assert.Equal("targetKey", Assert.Throws<ArgumentNullException>(() => builder.UseEscalation((string)null!)).ParamName);
        Assert.Equal("targetKey", Assert.Throws<ArgumentException>(() => builder.UseEscalation(string.Empty)).ParamName);
    }

    [Fact]
    public void Disposing_the_container_disposes_the_target_once()
    {
        var hosted = new AnsweringClient(TargetAnswers);
        var local = new AnsweringClient(UnsurePrimary);
        var services = new ServiceCollection();
        services.AddDecisionClient("hosted", _ => hosted);
        services.AddDecisionClient(_ => local).UseEscalation("hosted");
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IDecisionClient>();

        provider.Dispose();

        Assert.Equal(1, local.Disposals);
        Assert.Equal(1, hosted.Disposals);
    }

    [Fact]
    public async Task Retries_and_caching_under_the_escalation_stage_compose()
    {
        // The primary fails once with a transient error, which the retry stage retries; the cache then answers the repeat.
        var local = new AnsweringClient(UnsurePrimary, failFirst: true);
        var hosted = new AnsweringClient(TargetAnswers);
        var services = new ServiceCollection();
        services.AddMemoryCache();
        services.AddDecisionClient("hosted", _ => hosted);
        services.AddDecisionClient(_ => local)
            .UseEscalation("hosted")
            .UseRetries(o =>
            {
                o.InitialBackoff = TimeSpan.FromMilliseconds(1);
                o.Jitter = false;
            })
            .UseCaching();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var triage = Triage();

        var first = (await client.EvaluateAsync(new DecisionRequest(triage, State))).Value;
        var second = (await client.EvaluateAsync(new DecisionRequest(triage, State))).Value;

        Assert.Equal(2, local.Calls);
        Assert.Equal(2, hosted.Calls);
        foreach (var response in new[] { first, second })
        {
            Assert.Equal(
                [
                    new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 0.97, null),
                    new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.55, 0.8),
                ],
                response.Path!.Routes);
            Assert.Equal(0.97, response.Answers[0].Probability);
            Assert.Equal(1, response.Answers[1].ChosenIndex);
        }

        Assert.Equal(
            [new DecisionCall("primary", null, null, IsCached: false), new DecisionCall("hosted", null, null, IsCached: false)],
            first.Path!.Calls);
        Assert.Equal(
            [new DecisionCall("primary", null, null, IsCached: true), new DecisionCall("hosted", null, null, IsCached: false)],
            second.Path!.Calls);
        Assert.False(second.IsCached);
    }

    /// <summary>Answers each question by key, optionally failing the first call with a transient error.</summary>
    private sealed class AnsweringClient(IReadOnlyDictionary<string, QuestionAnswer> answers, bool failFirst = false) : IDecisionClient
    {
        private int _calls;

        public int Calls => _calls;

        public int Disposals { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _calls) == 1 && failFirst)
            {
                return new(new DecisionError(DecisionErrorKind.Server, "down"));
            }

            var definition = request.Definition;
            return new(new DecisionResponse(definition, [.. definition.Questions.Select(q => answers[q.Key])]));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose() => Disposals++;
    }
}
