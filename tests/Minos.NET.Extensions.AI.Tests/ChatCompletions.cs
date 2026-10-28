using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Minos.Extensions.AI.Tests;

/// <summary>Chat responses shaped as the OpenAI SDK returns them, with a hand-built <see cref="ChatCompletion"/> underneath.</summary>
internal static class ChatCompletions
{
    /// <summary>
    /// A response whose first token is the first of <paramref name="top"/>, with <paramref name="top"/> as that token's
    /// top logprobs.
    /// </summary>
    public static ChatResponse WithTopLogprobs(params (string Token, float Logprob)[] top)
    {
        var first = OpenAIChatModelFactory.ChatTokenLogProbabilityDetails(
            top[0].Token,
            top[0].Logprob,
            utf8Bytes: null,
            topLogProbabilities: top.Select(entry => OpenAIChatModelFactory.ChatTokenTopLogProbabilityDetails(entry.Token, entry.Logprob, utf8Bytes: null)).ToArray());
        return Respond(top[0].Token, [first]);
    }

    /// <summary>A response whose first token has its own logprob but no top logprobs.</summary>
    public static ChatResponse WithFirstTokenOnly(string token, float logprob)
        => Respond(token, [OpenAIChatModelFactory.ChatTokenLogProbabilityDetails(token, logprob, utf8Bytes: null, topLogProbabilities: [])]);

    /// <summary>A response that carries no logprobs, as from a model or provider that does not return them.</summary>
    public static ChatResponse WithoutLogprobs(string text = "A") => Respond(text, []);

    private static ChatResponse Respond(string text, ChatTokenLogProbabilityDetails[] logprobs)
    {
        // Every argument is given, so the stable overload is chosen over the evaluation-only one with more parameters.
        var completion = OpenAIChatModelFactory.ChatCompletion(
            id: "chatcmpl-test",
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
