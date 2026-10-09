using Xunit.Abstractions;

namespace Minos.Live.Tests;

/// <summary>Shared infrastructure for the live smoke tests: an owned client, the model and a probe request.</summary>
internal static class Live
{
    /// <summary>The alias for the newest release of any kind, which TypeSafe lists.</summary>
    public const string Preview = "jev-preview";

    private const string State = "Help! My payouts have been failing for 3 days and nobody answers.";

    private static readonly string[] TeamOptions = ["billing", "technical", "sales"];

    /// <summary>
    /// Creates an owned <see cref="DecisionClient"/> whose key comes from the environment. <see
    /// cref="DecisionClientOptions.MaxRetries"/> is 0, so each evaluation bills at most once and a transient failure
    /// surfaces instead of being hidden by a retry. <see cref="DecisionClientOptions.Model"/> is set from <see
    /// cref="Model"/>, so a typed call honours <c>JEV_LIVE_MODEL</c> and <c>JEV_LIVE_OPENROUTER_MODEL</c> the same
    /// way <see cref="Request"/> does for the untyped tests.
    /// </summary>
    /// <param name="provider">The provider to call.</param>
    public static DecisionClient Client(DecisionProvider provider)
        => new(new DecisionClientOptions { Provider = provider, MaxRetries = 0, Model = Model(provider) });

    /// <summary>
    /// Gets the model to send: <c>JEV_LIVE_OPENROUTER_MODEL</c> for OpenRouter, else <c>JEV_LIVE_MODEL</c>, else
    /// <see cref="DecisionDefaults.Model"/>.
    /// </summary>
    /// <param name="provider">The provider the model is sent to.</param>
    public static string Model(DecisionProvider provider)
    {
        if (provider == DecisionProvider.OpenRouter
            && Environment.GetEnvironmentVariable("JEV_LIVE_OPENROUTER_MODEL") is { Length: > 0 } openRouterModel)
        {
            return openRouterModel;
        }

        return Environment.GetEnvironmentVariable("JEV_LIVE_MODEL") is { Length: > 0 } model
            ? model
            : DecisionDefaults.Model;
    }

    /// <summary>Builds a probe request with one Noul, one Choice and one Score question.</summary>
    /// <param name="provider">The provider the request is sent to; picks the model.</param>
    public static SystemOneRequest Request(DecisionProvider provider) => Request(Model(provider));

    /// <summary>Builds the probe request for a given model id or alias.</summary>
    /// <param name="model">The model to send, such as <c>jev-preview</c> or a versioned id.</param>
    public static SystemOneRequest Request(string model) => new()
    {
        State = State,
        Model = model,
        Questions = new Dictionary<string, Question>
        {
            ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            ["team"] = new ChoiceQuestion
            {
                Instructions = "Which team should handle this?",
                Criteria = new Dictionary<string, DecisionContent?>
                {
                    ["billing"] = "Payments, invoicing, refunds",
                    ["technical"] = "Bugs, outages, integrations",
                    ["sales"] = "Pricing, upgrades, new accounts",
                },
            },
            ["frustration"] = new ScoreQuestion
            {
                Instructions = "How frustrated is the customer?",
                Criteria = ["Calm", "Frustrated", "Angry"],
            },
        },
    };

    /// <summary>Asserts that a successful response answered every question in <see cref="Request"/> plausibly.</summary>
    /// <param name="response">The response to check.</param>
    public static void AssertAnsweredEveryQuestion(SystemOneResponse response)
    {
        var urgent = Assert.IsType<NoulAnswer>(response.Answers["is_urgent"]);
        Assert.InRange(urgent.Noul, 0.0, 1.0);

        var team = Assert.IsType<ChoiceAnswer>(response.Answers["team"]);
        Assert.Contains(team.Choice, TeamOptions);

        var frustration = Assert.IsType<ScoreAnswer>(response.Answers["frustration"]);
        var nearestLevel = ((int)Math.Round(frustration.Score)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Assert.Contains(nearestLevel, frustration.Legend.Keys);

        Assert.True(response.Usage.InputTokens > 0);
    }

    /// <summary>
    /// Logs a failed call's public fields: kind, status, retry-after and the JSON detail body. Never the request's
    /// <c>Authorization</c> value, which <see cref="DecisionError"/> never carries.
    /// </summary>
    /// <param name="output">Where to write.</param>
    /// <param name="error">The failure to log.</param>
    public static void LogError(ITestOutputHelper output, DecisionError error)
    {
        output.WriteLine("Kind: " + error.Kind.ToString());
        output.WriteLine("Status: " + (error.StatusCode?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "(none)"));
        output.WriteLine("Retry-After: " + (error.RetryAfter?.ToString() ?? "(none, header absent)"));
        output.WriteLine("Body: " + (error.Detail is { } detail ? detail.ToString() : "(not JSON, or no body)"));
    }

    /// <summary>Logs the model a request named and the model the provider reports as having answered.</summary>
    /// <param name="output">Where to write.</param>
    /// <param name="requested">The model id or alias the request named.</param>
    /// <param name="response">The response, whose <see cref="SystemOneResponse.Model"/> names the model that answered.</param>
    public static void LogServedModel(ITestOutputHelper output, string requested, SystemOneResponse response)
        => output.WriteLine("Requested " + requested + ", answered by " + response.Model);
}
