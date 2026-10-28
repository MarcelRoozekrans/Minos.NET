using System.ClientModel;
using Microsoft.Extensions.AI;
using Minos.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Minos.AotSmoke;

/// <summary>
/// The chat-client bridge under Native AOT: an OpenAI SDK <see cref="ChatClient"/> wrapped with <c>AsIChatClient()</c> and
/// then <c>AsDecisionClient()</c>, its services, the checks it makes before anything is sent, and its disposal, which
/// leaves the chat client alone. Nothing is sent: the key and the endpoint are dummies.
/// </summary>
internal static class ExtensionsAIChecks
{
    private static readonly Uri Endpoint = new("https://example.test/v1/");

    [Covers("static Minos.Extensions.AI.ChatClientDecisionExtensions.AsDecisionClient(this Microsoft.Extensions.AI.IChatClient! chatClient, Minos.Extensions.AI.ChatDecisionClientOptions? options = null) -> Minos.Extensions.AI.ChatDecisionClient!")]
    [Covers("Minos.Extensions.AI.ChatDecisionClient.GetService(System.Type! serviceType, object? serviceKey = null) -> object?")]
    public static void BridgeOverTheOpenAIChatClientAnswersItsServices()
    {
        using var chatClient = OpenAIChatClient().AsIChatClient();
        using var client = chatClient.AsDecisionClient();

        var capabilities = client.GetService(typeof(DecisionCapabilities)) as DecisionCapabilities;
        var metadata = client.GetService(typeof(DecisionClientMetadata)) as DecisionClientMetadata;

        Program.Check(
            capabilities is { QuestionKinds: QuestionKinds.All, MaxQuestions: null, MaxChoiceOptions: 26, SupportsImages: false, SupportsModelListing: false }
                && metadata is { DefaultModel: "smoke-model" }
                && ReferenceEquals(client.GetService(typeof(ChatDecisionClient)), client)
                && client.GetService(typeof(ChatClientMetadata)) is ChatClientMetadata,
            "an OpenAI ChatClient wrapped with AsIChatClient().AsDecisionClient() answers its capabilities, its metadata and the chat client's services, under Native AOT");
    }

    [Covers("Minos.Extensions.AI.ChatDecisionClient.ChatDecisionClient(Microsoft.Extensions.AI.IChatClient! chatClient, Minos.Extensions.AI.ChatDecisionClientOptions? options = null) -> void")]
    [Covers("Minos.Extensions.AI.ChatDecisionClientOptions.ChatDecisionClientOptions() -> void")]
    [Covers("Minos.Extensions.AI.ChatDecisionClient.EvaluateAsync(Minos.DecisionRequest! request) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.Extensions.AI.ChatDecisionClient.EvaluateAsync(Minos.DecisionRequest! request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.DecisionResponse!, Minos.DecisionError!>>")]
    [Covers("Minos.Extensions.AI.ChatDecisionClient.Dispose() -> void")]
    public static async Task RequestsTheBridgeCannotTakeAreNotSent()
    {
        using var chatClient = OpenAIChatClient().AsIChatClient();
        var client = new ChatDecisionClient(chatClient, new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs, Model = "smoke-model" });
        using var cancellation = new CancellationTokenSource();

        var empty = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(), SmokeAnswers.State)).ConfigureAwait(false);
        var withImage = await client.EvaluateAsync(
            new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State) { Images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")] },
            cancellation.Token).ConfigureAwait(false);
        client.Dispose();
        client.Dispose();

        Program.Check(
            empty.IsFailure && empty.Error.Kind == DecisionErrorKind.Validation
                && withImage.IsFailure && withImage.Error.Kind == DecisionErrorKind.Unsupported
                && withImage.Error.Failures is [{ Rule: "MIN206" }]
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => client.EvaluateAsync(new DecisionRequest(SmokeTriage.Definition, SmokeAnswers.State)).AsTask()).ConfigureAwait(false)
                && chatClient.GetService(typeof(ChatClientMetadata)) is ChatClientMetadata,
            "the bridge answers an empty set with Validation and images with Unsupported without sending, then disposes twice, refuses calls and leaves the chat client usable, under Native AOT");
    }

    private static ChatClient OpenAIChatClient()
        => new("smoke-model", new ApiKeyCredential("smoke-key"), new OpenAIClientOptions { Endpoint = Endpoint });
}
