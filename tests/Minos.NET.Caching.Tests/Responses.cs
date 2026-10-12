using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Minos.Caching.Tests;

internal static class Responses
{
    public static readonly QuestionSetDefinition Triage = new(
        QuestionDefinition.Noul("is_urgent", "Is this urgent?"),
        QuestionDefinition.Choice(
            "department",
            "Which team should handle this?",
            new OptionDefinition("billing", "Payments"),
            new OptionDefinition("tech", "Bugs"),
            new OptionDefinition("other", null)),
        QuestionDefinition.Score("severity", "How severe is this?", "Cosmetic", "Disruptive", "Blocking"));

    public static DecisionRequest Request(string state = "I was charged twice.") => new(Triage, state);

    /// <summary>A read response, with an id and an upstream provider, so every stored field has a value.</summary>
    public static DecisionResponse Answer()
    {
        var built = new DecisionResponse(
            Triage,
            [
                QuestionAnswer.Noul(0.9),
                QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
                QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9]),
            ],
            model: "jev-1.13.0",
            usage: new DecisionUsage { InputTokens = 296, OutputTokens = 20, Cost = 0.0012 });
        return new DecisionResponse(
            Triage, built.Slots, built.Probabilities, built.Model, built.InputTokens, built.OutputTokens, built.Cost, "gen-0001", "Together");
    }

    /// <summary>A fresh, empty in-process cache.</summary>
    public static MemoryCache Memory() => new(new MemoryCacheOptions());

    /// <summary>Services with an in-process cache, and <paramref name="distributed"/> as the distributed cache when one is given.</summary>
    public static ServiceProvider Services(IDistributedCache? distributed = null)
    {
        var services = new ServiceCollection();
        services.AddMemoryCache();
        if (distributed is not null)
        {
            services.AddSingleton(distributed);
        }

        return services.BuildServiceProvider();
    }
}
