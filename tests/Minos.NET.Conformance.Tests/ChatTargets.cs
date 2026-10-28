using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Minos.Extensions.AI;
using Minos.SystemOne;
using OpenAI;
using OpenAI.Chat;

namespace Minos.Conformance.Tests;

/// <summary>
/// The chat-completions targets: the bridge over the OpenAI SDK's chat client, pointed at OpenRouter, reading logprobs.
/// The replay suite builds it with a stand-in key; the live record test, which links this file, reads the key from the
/// environment. The SDK never retries, so each question bills at most once.
/// </summary>
internal static class ChatTargets
{
    /// <summary>The OpenRouter chat target's name, and its fixture's.</summary>
    public const string OpenRouterName = "chat-openrouter";

    /// <summary>The model the replay suite asks, and the live test asks unless <c>MINOS_LIVE_CHAT_MODEL</c> is set.</summary>
    public const string DefaultModel = "openai/gpt-4.1-mini";

    private static readonly Uri OpenRouterEndpoint = new("https://openrouter.ai/api/v1");

    /// <summary>
    /// The reported model is the requested one, or that id with a dated or versioned suffix: a dash and a digit. OpenRouter
    /// reports the provider-prefixed id, such as <c>openai/gpt-4.1-mini</c> or <c>openai/gpt-4.1-mini-2025-04-14</c>.
    /// A reported model without a provider prefix is compared with the requested id without its prefix, so
    /// <c>gpt-4.1-mini-2025-04-14</c> fits too. One with another provider's prefix does not fit.
    /// </summary>
    public static ModelRule ModelRule { get; } = new(
        "The reported model is the requested one, or that id followed by a dash and a dated or versioned suffix, such as openai/gpt-4.1-mini-2025-04-14 for openai/gpt-4.1-mini; a reported model without a provider prefix is compared without the requested one's.",
        static (requested, reported) => Fits(requested, reported)
            || (!reported.Contains('/', StringComparison.Ordinal) && Fits(WithoutProvider(requested), reported))
            || (!requested.Contains('/', StringComparison.Ordinal) && Fits(requested, WithoutProvider(reported))));

    /// <summary>Builds the OpenRouter chat target.</summary>
    /// <param name="model">The model every probe asks for.</param>
    /// <param name="maxRetries">How often the SDK retries a failed call.</param>
    /// <param name="apiKey">The key, or <see langword="null"/> to read <c>OPENROUTER_API_KEY</c> when the client is built.</param>
    public static ConformanceTarget OpenRouter(string model, int maxRetries, string? apiKey = null)
        => new(OpenRouterName, ConformanceWire.ChatCompletions, model, ModelRule, http =>
        {
            var key = apiKey
                ?? (Environment.GetEnvironmentVariable(SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable) is { Length: > 0 } configured ? configured : null)
                ?? throw new InvalidOperationException("Set " + SystemOneDefaults.OpenRouterApiKeyEnvironmentVariable + " to build the chat target.");
            var chat = new ChatClient(model, new ApiKeyCredential(key), new OpenAIClientOptions
            {
                Endpoint = OpenRouterEndpoint,
                Transport = new HttpClientPipelineTransport(http),
                RetryPolicy = new ClientRetryPolicy(maxRetries),
            });
            return chat.AsIChatClient().AsDecisionClient(new() { ProbabilityMode = ProbabilityMode.Logprobs, Model = model });
        });

    // The same id, or the id, a dash and a digit: gpt-4.1-mini-2025-04-14 fits gpt-4.1-mini, but gpt-4.1-mini does not fit gpt-4.1.
    private static bool Fits(string requested, string reported)
        => string.Equals(requested, reported, StringComparison.Ordinal)
            || (reported.Length > requested.Length + 1
                && reported.StartsWith(requested, StringComparison.Ordinal)
                && reported[requested.Length] == '-'
                && char.IsAsciiDigit(reported[requested.Length + 1]));

    // The text after the first slash, or the whole model when it has none.
    private static string WithoutProvider(string model)
    {
        var slash = model.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? model : model[(slash + 1)..];
    }
}
