using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
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

    public static DecisionResponse Answer()
        => new(
            Triage,
            [
                QuestionAnswer.Noul(0.9),
                QuestionAnswer.Choice(1, 0.7, [0.2, 0.7, 0.1]),
                QuestionAnswer.Score(2, 1.9, 0.8, [0.0, 0.1, 0.9]),
            ],
            model: "jev-1.13.0",
            usage: new DecisionUsage { InputTokens = 296, OutputTokens = 20, Cost = 0.0012 });

    /// <summary>A HybridCache with the decision serializer, over <paramref name="distributed"/> as L2 when one is given.</summary>
    public static ServiceProvider Services(IDistributedCache? distributed = null)
    {
        var services = new ServiceCollection();
        if (distributed is not null)
        {
            services.AddSingleton(distributed);
        }

        services.AddHybridCache().AddDecisionSerializer();
        return services.BuildServiceProvider();
    }

    public static HybridCache Cache(this ServiceProvider services) => services.GetRequiredService<HybridCache>();
}
