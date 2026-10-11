using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="SystemOneCapabilities"/> under Native AOT: a question set the provider does not accept is refused before it
/// is sent, and the resolved capabilities are read back. The supported path is held to 0 B by every existing typed and
/// built-set allocation gate, which run the same check; this path gets a functional check, not a budget.
/// </summary>
internal static class CapabilityChecks
{
    private static readonly QuestionSetDefinition NoulAndChoice = new(
        QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"),
        QuestionDefinition.Choice("topic", "What is the topic?", new OptionDefinition("billing", "Payments"), new OptionDefinition("other", "Anything else")));

    [Covers("Minos.SystemOne.SystemOneCapabilities.SystemOneCapabilities() -> void")]
    public static async Task UnsupportedQuestionKindIsNotSent()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, Program.NoulResponse);
        using var http = new HttpClient(handler);
        using var client = new SystemOneClient(http, new SystemOneClientOptions
        {
            Provider = SystemOneProvider.Custom,
            BaseAddress = new Uri("https://example.test/api/"),
            Model = "jev-1.13.0",
            MaxRetries = 2,
            Capabilities = new SystemOneCapabilities { QuestionKinds = SystemOneQuestionKinds.Noul },
        });

        var result = await client.EvaluateAsync(new DecisionRequest(NoulAndChoice, SmokeAnswers.State)).ConfigureAwait(false);

        Program.Check(
            result.IsFailure
                && result.Error.Kind == DecisionErrorKind.Unsupported
                && string.Equals(result.Error.Message, "Custom does not support Choice questions.", StringComparison.Ordinal)
                && result.Error.Failures.Count == 1
                && string.Equals(result.Error.Failures[0].Rule, "MIN201", StringComparison.Ordinal)
                && string.Equals(result.Error.Failures[0].QuestionKey, "topic", StringComparison.Ordinal)
                && handler.Calls == 0,
            "a Choice question on a Noul-only provider is Unsupported and no request is sent under Native AOT");

        var capabilities = client.GetService<SystemOneCapabilities>();
        Program.Check(
            capabilities is { QuestionKinds: SystemOneQuestionKinds.Noul, MaxQuestions: null, MaxChoiceOptions: null, Images: false, ModelListing: true }
                && !ReferenceEquals(capabilities, client.GetService<SystemOneCapabilities>()),
            "GetService returns the resolved capabilities as a new instance under Native AOT");
    }

    public static void CapabilitiesBindFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Provider"] = "Custom",
                ["BaseAddress"] = "https://example.test/api/",
                ["Model"] = "jev-1.13.0",
                ["Capabilities:QuestionKinds"] = "Noul, Choice",
                ["Capabilities:MaxQuestions"] = "8",
                ["Capabilities:ModelListing"] = "false",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSystemOneClient(configuration).HttpClient.ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, Program.NoulResponse));
        using var provider = services.BuildServiceProvider();

        var capabilities = provider.GetRequiredService<IDecisionClient>().GetService<SystemOneCapabilities>();

        Program.Check(
            capabilities is { MaxQuestions: 8, MaxChoiceOptions: null, ModelListing: false }
                && capabilities.QuestionKinds == (SystemOneQuestionKinds.Noul | SystemOneQuestionKinds.Choice),
            "AddSystemOneClient binds Capabilities, the question kinds from a comma-separated list, under Native AOT");
    }
}
