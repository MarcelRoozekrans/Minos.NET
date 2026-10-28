using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Minos.Extensions.AI;

/// <summary>Reads the first output token's logprobs from the OpenAI SDK response under a chat response.</summary>
internal static class LogprobReader
{
    /// <summary>
    /// The top logprobs of the first output token. When the provider sent the token's logprob without a top list,
    /// that token alone. Returns null when the raw response is not an OpenAI <see cref="ChatCompletion"/> or carries
    /// no content logprobs.
    /// </summary>
    internal static IEnumerable<(string Token, double Logprob)>? TopAtFirstToken(ChatResponse response)
    {
        if (response.RawRepresentation is not ChatCompletion { ContentTokenLogProbabilities: { Count: > 0 } logprobs })
        {
            return null;
        }

        var first = logprobs[0];
        if (first.TopLogProbabilities.Count == 0)
        {
            return [(first.Token, first.LogProbability)];
        }

        return first.TopLogProbabilities.Select(top => (top.Token, (double)top.LogProbability));
    }
}
