using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Results;

namespace Minos.DependencyInjection.Tests;

/// <summary>The by-key <c>UseFallback</c>, which asks keyed clients registered with <c>AddDecisionClient</c> after the inner one fails.</summary>
public sealed class FallbackRegistrationTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static QuestionSetDefinition Triage() => new(
        QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")));

    [Fact]
    public async Task Keyed_steps_resolve_from_the_container()
    {
        var backup = new TestClient(fail: false);
        var services = new ServiceCollection();
        services.AddDecisionClient("backup", _ => backup);
        services.AddDecisionClient(_ => new TestClient(fail: true))
            .UseFallback(new KeyedFallbackStep("backup", TimeSpan.Zero));
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync(new DecisionRequest(Triage(), State));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, backup.Calls);
    }

    [Fact]
    public async Task Steps_are_named_by_their_keys()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient("first", _ => new TestClient(fail: true));
        services.AddDecisionClient("second", _ => new TestClient(fail: false));
        services.AddDecisionClient(_ => new TestClient(fail: true))
            .UseFallback(new KeyedFallbackStep("first", TimeSpan.FromMilliseconds(5)), new KeyedFallbackStep("second", TimeSpan.FromMilliseconds(7)));
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        var response = (await client.EvaluateAsync(new DecisionRequest(Triage(), State))).Value;

        var options = client.GetService<FallbackOptions>()!;
        Assert.Equal(["first", "second"], options.Steps.Select(s => s.Name));
        Assert.Equal([TimeSpan.FromMilliseconds(5), TimeSpan.FromMilliseconds(7)], options.Steps.Select(s => s.ExpectedLatency));
        Assert.Equal(["primary", "first", "second"], response.Path!.Calls.Select(c => c.ClientName));
    }

    [Fact]
    public void A_missing_key_throws_naming_it()
    {
        var services = new ServiceCollection();
        services.AddDecisionClient(_ => new TestClient(fail: true))
            .UseFallback(new KeyedFallbackStep("nowhere", TimeSpan.Zero));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("nowhere", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Configure_sets_the_primary_name_budget_and_predicate()
    {
        Func<DecisionError, bool> predicate = _ => true;
        var services = new ServiceCollection();
        services.AddDecisionClient("backup", _ => new TestClient(fail: false));
        services.AddDecisionClient(_ => new TestClient(fail: true))
            .UseFallback(
                o =>
                {
                    o.PrimaryName = "local";
                    o.Budget = TimeSpan.FromSeconds(3);
                    o.ShouldFallBack = predicate;
                },
                new KeyedFallbackStep("backup", TimeSpan.Zero));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IDecisionClient>().GetService<FallbackOptions>()!;

        Assert.Equal("local", options.PrimaryName);
        Assert.Equal(TimeSpan.FromSeconds(3), options.Budget);
        Assert.Same(predicate, options.ShouldFallBack);
        Assert.Equal("backup", Assert.Single(options.Steps).Name);
    }

    private sealed class TestClient(bool fail) : IDecisionClient
    {
        private int _calls;

        public int Calls => _calls;

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            if (fail)
            {
                return new(new DecisionError(DecisionErrorKind.Server, "down"));
            }

            var answers = request.Definition.Questions.Select(_ => QuestionAnswer.Noul(0.9)).ToArray();
            return new(new DecisionResponse(request.Definition, answers));
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
