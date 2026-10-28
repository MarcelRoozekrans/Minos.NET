using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Minos.AotSmoke;

/// <summary>
/// The chat-client bridge under Native AOT: an OpenAI SDK <see cref="ChatClient"/> wrapped with <c>AsIChatClient()</c> and
/// then <c>AsDecisionClient()</c>, its services, the checks it makes before anything is sent, and its disposal, which
/// leaves the chat client alone. The key and the endpoint are dummies: a request either stops before it is sent, or goes
/// to an in-process handler with a canned chat completion.
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

    [Covers("static Microsoft.Extensions.DependencyInjection.ChatDecisionClientServiceCollectionExtensions.AddChatDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services) -> Minos.DecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.ChatDecisionClientServiceCollectionExtensions.AddChatDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, System.Action<Minos.Extensions.AI.ChatDecisionClientOptions!>! configure) -> Minos.DecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.ChatDecisionClientServiceCollectionExtensions.AddChatDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name) -> Minos.DecisionClientServiceBuilder!")]
    [Covers("static Microsoft.Extensions.DependencyInjection.ChatDecisionClientServiceCollectionExtensions.AddChatDecisionClient(this Microsoft.Extensions.DependencyInjection.IServiceCollection! services, string! name, System.Action<Minos.Extensions.AI.ChatDecisionClientOptions!>! configure) -> Minos.DecisionClientServiceBuilder!")]
    public static void RegistrationsResolveTheDecisionClientsOverTheContainerChatClients()
    {
        using var defaultChat = OpenAIChatClient().AsIChatClient();
        using var namedChat = OpenAIChatClient().AsIChatClient();
        using var configuredChat = OpenAIChatClient().AsIChatClient();
        using var namedConfiguredChat = OpenAIChatClient().AsIChatClient();

        var plain = new ServiceCollection();
        plain.AddSingleton(defaultChat);
        plain.AddKeyedSingleton("named", namedChat);
        plain.AddChatDecisionClient();
        plain.AddChatDecisionClient("named");
        using var plainProvider = plain.BuildServiceProvider();

        var configured = new ServiceCollection();
        configured.AddSingleton(configuredChat);
        configured.AddKeyedSingleton("named", namedConfiguredChat);
        configured.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized);
        configured.AddChatDecisionClient("named", options => options.Model = "configured-model");
        using var configuredProvider = configured.BuildServiceProvider();

        var defaultClient = plainProvider.GetRequiredService<IDecisionClient>();
        var namedClient = plainProvider.GetRequiredKeyedService<IDecisionClient>("named");
        var configuredClient = configuredProvider.GetRequiredService<IDecisionClient>();
        var namedConfiguredClient = configuredProvider.GetRequiredKeyedService<IDecisionClient>("named");

        Program.Check(
            defaultClient.GetService<ChatDecisionClient>() is not null
                && namedClient.GetService<ChatDecisionClient>() is not null
                && configuredClient.GetService<ChatDecisionClient>() is not null
                && namedConfiguredClient.GetService(typeof(DecisionClientMetadata)) is DecisionClientMetadata { DefaultModel: "configured-model" },
            "AddChatDecisionClient registers a default and a keyed decision client over the container's chat clients, with their own options, under Native AOT");
    }

    // A chat completion whose first token is "A", with "A" and "B" among its top logprobs.
    private const string LogprobCompletion =
        """{"id":"chatcmpl-smoke","object":"chat.completion","created":0,"model":"smoke-model","choices":[{"index":0,"message":{"role":"assistant","content":"A"},"logprobs":{"content":[{"token":"A","logprob":-0.1,"bytes":null,"top_logprobs":[{"token":"A","logprob":-0.1,"bytes":null},{"token":"B","logprob":-2.4,"bytes":null}]}],"refusal":null},"finish_reason":"length"}],"usage":{"prompt_tokens":10,"completion_tokens":1,"total_tokens":11}}""";

    // A chat completion whose text is the verbalized JSON for three options.
    private const string VerbalizedCompletion =
        """{"id":"chatcmpl-smoke","object":"chat.completion","created":0,"model":"smoke-model","choices":[{"index":0,"message":{"role":"assistant","content":"{\"A\":0.7,\"B\":0.2,\"C\":0.1}"},"logprobs":null,"finish_reason":"stop"}],"usage":{"prompt_tokens":12,"completion_tokens":9,"total_tokens":21}}""";

    public static async Task BothPathsReadARealChatCompletion()
    {
        var refund = new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"));
        var team = new QuestionSetDefinition(QuestionDefinition.Choice(
            "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null)));

        var logprobHandler = new CapturingHandler(LogprobCompletion);
        using var logprobChat = OpenAIChatClient(logprobHandler).AsIChatClient();
        using var logprobClient = logprobChat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Logprobs });
        var logprobs = await logprobClient.EvaluateAsync(new DecisionRequest(refund, SmokeAnswers.State)).ConfigureAwait(false);
        using var logprobRequest = logprobHandler.Body();

        var verbalizedHandler = new CapturingHandler(VerbalizedCompletion);
        using var verbalizedChat = OpenAIChatClient(verbalizedHandler).AsIChatClient();
        using var verbalizedClient = verbalizedChat.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Verbalized });
        var verbalized = await verbalizedClient.EvaluateAsync(new DecisionRequest(team, SmokeAnswers.State)).ConfigureAwait(false);
        using var verbalizedRequest = verbalizedHandler.Body();

        // P(A) is the softmax of -0.1 against -2.4.
        var expectedYes = 1 / (1 + Math.Exp(-2.3));
        Program.Check(
            logprobs.IsSuccess
                && logprobs.Value.Answers[0] is { ProbabilitySource: ProbabilitySource.Logprobs } yes
                && Math.Abs(yes.Probability - expectedYes) < 1e-6
                && logprobRequest.RootElement.GetProperty("logprobs").GetBoolean()
                && logprobRequest.RootElement.GetProperty("top_logprobs").GetInt32() == 20
                && verbalized.IsSuccess
                && verbalized.Value.Answers[0] is { ProbabilitySource: ProbabilitySource.Verbalized, ChosenIndex: 0 } picked
                && Math.Abs(picked.Confidence - 0.7) < 1e-9
                && string.Equals(verbalizedRequest.RootElement.GetProperty("response_format").GetProperty("type").GetString(), "json_schema", StringComparison.Ordinal)
                && !verbalizedRequest.RootElement.TryGetProperty("temperature", out _),
            "an OpenAI ChatClient over an in-process transport answers through AsIChatClient().AsDecisionClient(): the logprob request and its top logprobs, and the verbalized JSON schema and its reply, under Native AOT");
    }

    private static ChatClient OpenAIChatClient(HttpMessageHandler handler)
        => new("smoke-model", new ApiKeyCredential("smoke-key"), new OpenAIClientOptions
        {
            Endpoint = Endpoint,
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
        });

    private static ChatClient OpenAIChatClient()
        => new("smoke-model", new ApiKeyCredential("smoke-key"), new OpenAIClientOptions { Endpoint = Endpoint });
}
