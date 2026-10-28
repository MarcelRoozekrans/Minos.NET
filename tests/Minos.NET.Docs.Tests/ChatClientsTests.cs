using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos.Extensions.AI;
using Minos.Recording;
using AIChatMessage = Microsoft.Extensions.AI.ChatMessage;
using CompletionContent = OpenAI.Chat.ChatMessageContent;
using CompletionFinishReason = OpenAI.Chat.ChatFinishReason;
using CompletionRole = OpenAI.Chat.ChatMessageRole;
using CompletionTokenLogprob = OpenAI.Chat.ChatTokenLogProbabilityDetails;
using OpenAIChatModels = OpenAI.Chat.OpenAIChatModelFactory;

namespace Minos.Docs.Tests;

/// <summary>The chat-clients page's examples run over a scripted chat client, and do what the page says they do.</summary>
public sealed class ChatClientsTests : IDisposable
{
    private const string Ticket = "I was charged twice for my March invoice and I want my money back.";

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("minos-docs-chat-clients-");

    [Fact]
    public async Task AsDecisionClient_ReadsTheLogprobs_AndLeavesTheChatClientUndisposed()
    {
        using var chat = new TicketChat(logprobs: true);

        var answer = await ChatDecisions.TriageAsync(chat, Ticket, CancellationToken.None);

        Assert.Equal("team: Billing, from Logprobs", answer);
        Assert.False(chat.Disposed);
        Assert.Equal(2, chat.LogprobCalls);
        Assert.Equal(0, chat.VerbalizedCalls);
    }

    [Fact]
    public async Task Auto_AsksAgainVerbalized_WhenTheReplyHasNoLogprobs()
    {
        using var chat = new TicketChat(logprobs: false);

        var answer = await ChatDecisions.TriageAsync(chat, Ticket, CancellationToken.None);

        Assert.Equal("team: Billing, from Verbalized", answer);
        Assert.Equal(2, chat.LogprobCalls);
        Assert.Equal(2, chat.VerbalizedCalls);
    }

    [Fact]
    public async Task TheRegistration_AsksVerbalizedOnly_AndRecordsEachAnswersSource()
    {
        var path = Path.Combine(_directory.FullName, "decisions.jsonl");
        using var chat = new TicketChat(logprobs: true);
        var services = new ServiceCollection().AddChatDecisions(chat, path);

        await using (var provider = services.BuildServiceProvider())
        {
            var result = await provider.GetRequiredService<IDecisionClient>().EvaluateAsync<ChatTicketCheck>(Ticket);

            Assert.True(result.IsSuccess);
            Assert.Equal(ProbabilitySource.Verbalized, result.Value.WantsRefund.ProbabilitySource);
            Assert.Equal(ProbabilitySource.Verbalized, result.Value.Team.ProbabilitySource);
        }

        // Disposing the container disposed the recorder, which wrote the record. It is version 2, with each answer's source.
        Assert.Equal(0, chat.LogprobCalls);
        var line = Assert.Single(await File.ReadAllLinesAsync(path));
        using var record = JsonDocument.Parse(line);
        Assert.Equal(2, record.RootElement.GetProperty("version").GetInt32());
        Assert.All(
            record.RootElement.GetProperty("answers").EnumerateArray(),
            answer => Assert.Equal("verbalized", answer.GetProperty("probabilitySource").GetString()));
        var decision = Assert.Single(await RecordFileReading.ReadAsync(path, CancellationToken.None));
        Assert.All(decision.Record.Answers, answer => Assert.Equal(ProbabilitySource.Verbalized, answer.ProbabilitySource));
    }

    [Theory]
    [InlineData(true, 0.85, true)]
    [InlineData(true, 0.95, false)]
    [InlineData(false, 0.85, false)]
    public async Task CanRouteAlone_NeverTrustsAVerbalizedAnswer(bool logprobs, double threshold, bool expected)
    {
        using var chat = new TicketChat(logprobs);
        using var client = chat.AsDecisionClient();
        var result = await client.EvaluateAsync<ChatTicketCheck>(Ticket);

        // Billing's probability is 0.9 on both paths.
        Assert.Equal(0.9, result.Value.Team.Confidence, 6);
        Assert.Equal(expected, TicketRouting.CanRouteAlone(result.Value.Team, threshold));
    }

    public void Dispose() => _directory.Delete(recursive: true);

    /// <summary>
    /// Answers the ticket's two questions. The one-token logprob call gets the labels' logprobs, or a reply without any;
    /// any other call gets the probabilities as the JSON object the verbalized prompt asks for.
    /// </summary>
    private sealed class TicketChat(bool logprobs) : IChatClient
    {
        private int _logprobCalls;
        private int _verbalizedCalls;

        public int LogprobCalls => _logprobCalls;

        public int VerbalizedCalls => _verbalizedCalls;

        public bool Disposed { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            var prompt = messages.Last().Text;
            double[] p = prompt.Contains("money back?", StringComparison.Ordinal) ? [0.8, 0.2] : [0.9, 0.1];
            if (options?.MaxOutputTokens == 1)
            {
                Interlocked.Increment(ref _logprobCalls);
                return Task.FromResult(Completed(logprobs ? p : null));
            }

            Interlocked.Increment(ref _verbalizedCalls);
            var json = "{" + string.Join(", ", p.Select((value, i) => string.Create(CultureInfo.InvariantCulture, $"\"{Label(i)}\": {value}"))) + "}";
            return Task.FromResult(new ChatResponse(new AIChatMessage(ChatRole.Assistant, json)));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AIChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() => Disposed = true;

        private static string Label(int index) => ((char)('A' + index)).ToString();

        // An OpenAI SDK completion of the first label, with every label among the top logprobs, or with no logprobs.
        private static ChatResponse Completed(double[]? p)
        {
            CompletionTokenLogprob[] tokens = [];
            if (p is not null)
            {
                var top = p.Select((value, i) => OpenAIChatModels.ChatTokenTopLogProbabilityDetails(Label(i), (float)Math.Log(value), utf8Bytes: null)).ToArray();
                tokens = [OpenAIChatModels.ChatTokenLogProbabilityDetails("A", (float)Math.Log(p[0]), utf8Bytes: null, topLogProbabilities: top)];
            }

            var completion = OpenAIChatModels.ChatCompletion(
                id: "chatcmpl-docs",
                finishReason: CompletionFinishReason.Length,
                content: new CompletionContent("A"),
                refusal: null,
                toolCalls: [],
                role: CompletionRole.Assistant,
                functionCall: null,
                contentTokenLogProbabilities: tokens,
                refusalTokenLogProbabilities: [],
                createdAt: DateTimeOffset.UnixEpoch,
                model: null,
                systemFingerprint: null,
                usage: null);
            return new ChatResponse(new AIChatMessage(ChatRole.Assistant, "A")) { RawRepresentation = completion };
        }
    }
}
