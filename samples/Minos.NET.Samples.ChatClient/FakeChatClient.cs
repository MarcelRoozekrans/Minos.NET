using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Minos.Samples.ChatClient;

/// <summary>
/// A chat client that answers the three example tickets without a network. It gives logprobs for the first ticket, as
/// OpenAI does, and plain JSON probabilities for the others, as a model without logprobs does, so a run shows both
/// probability sources.
/// </summary>
public sealed class FakeChatClient : IChatClient
{
    // Per ticket, in the order of Tickets.All: the probabilities of the refund question (yes, no) and of the team question
    // (billing, technical, account). A ticket added to Tickets.All needs a script here; the constructor checks it.
    private static readonly (double[] Refund, double[] Team)[] Scripts =
    [
        ([0.93, 0.07], [0.86, 0.09, 0.05]),
        ([0.02, 0.98], [0.04, 0.90, 0.06]),
        ([0.03, 0.97], [0.05, 0.25, 0.70]),
    ];

    private const string RefundQuestion = "Does the customer ask for money back?";

    /// <summary>Initializes a new instance of the <see cref="FakeChatClient"/> class.</summary>
    /// <exception cref="InvalidOperationException">Tickets.All and the scripts differ in number.</exception>
    public FakeChatClient()
    {
        if (Scripts.Length != Tickets.All.Count)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"The fake chat client has {Scripts.Length} scripts for {Tickets.All.Count} tickets; give each ticket in Tickets.All a script."));
        }
    }

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var prompt = messages.Last().Text;
        var ticket = FindTicket(prompt);
        if (ticket < 0)
        {
            throw new InvalidOperationException("The fake chat client only knows the example tickets.");
        }

        var script = Scripts[ticket];
        var p = prompt.Contains(RefundQuestion, StringComparison.Ordinal) ? script.Refund : script.Team;

        // The bridge asks for one token first. Only the first ticket gets logprobs; the others make it ask again, verbalized.
        var response = options?.MaxOutputTokens == 1
            ? (ticket == 0 ? WithLogprobs(p) : WithoutLogprobs())
            : Verbalized(p);
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<Microsoft.Extensions.AI.ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("The bridge does not stream.");

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private static int FindTicket(string prompt)
    {
        for (var i = 0; i < Tickets.All.Count; i++)
        {
            if (prompt.Contains(Tickets.All[i], StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static string Label(int index) => ((char)('A' + index)).ToString();

    // The labels A, B, ... as the top logprobs of the first token, best first.
    private static ChatResponse WithLogprobs(double[] p)
    {
        var order = Enumerable.Range(0, p.Length).OrderByDescending(i => p[i]).ToArray();
        var top = order
            .Select(i => OpenAIChatModelFactory.ChatTokenTopLogProbabilityDetails(Label(i), (float)Math.Log(p[i]), utf8Bytes: null))
            .ToArray();
        var first = OpenAIChatModelFactory.ChatTokenLogProbabilityDetails(Label(order[0]), (float)Math.Log(p[order[0]]), utf8Bytes: null, topLogProbabilities: top);
        return Completed(Label(order[0]), [first]);
    }

    // A reply with no logprobs, as from a model or provider that does not return them.
    private static ChatResponse WithoutLogprobs() => Completed("A", []);

    // The JSON object keyed by label that the verbalized prompt asks for.
    private static ChatResponse Verbalized(double[] p)
    {
        var fields = p.Select((value, i) => FormattableString.Invariant($"\"{Label(i)}\": {value}"));
        var text = "{" + string.Join(", ", fields) + "}";
        return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, text));
    }

    private static ChatResponse Completed(string text, ChatTokenLogProbabilityDetails[] logprobs)
    {
        // The bridge reads the logprobs from the OpenAI SDK's completion. Every argument is given, so the stable overload
        // is chosen over the evaluation-only one with more parameters.
        var completion = OpenAIChatModelFactory.ChatCompletion(
            id: "chatcmpl-fake",
            finishReason: OpenAI.Chat.ChatFinishReason.Length,
            content: new ChatMessageContent(text),
            refusal: null,
            toolCalls: [],
            role: ChatMessageRole.Assistant,
            functionCall: null,
            contentTokenLogProbabilities: logprobs,
            refusalTokenLogProbabilities: [],
            createdAt: DateTimeOffset.UnixEpoch,
            model: null,
            systemFingerprint: null,
            usage: null);
        return new ChatResponse(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, text)) { RawRepresentation = completion };
    }
}
