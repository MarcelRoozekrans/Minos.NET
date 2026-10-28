using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Minos.Conformance.Tests;

/// <summary>
/// Stands in for OpenRouter's chat completions: each request gets a chat.completion whose one token is the label the
/// script picks for its question, with that token's top logprobs. A request with the bad-key probe's key gets a 401 as
/// OpenRouter sends it. The answers depend only on the request, so a recording through it is the same every time.
/// </summary>
internal sealed class ScriptedChatNetwork : HttpMessageHandler
{
    // 2026-10-28T00:00:00Z.
    private const long Created = 1793145600;

    // The probability of each label, A first, by the question's instructions.
    private static readonly Dictionary<string, double[]> Script = new(StringComparer.Ordinal)
    {
        ["Does this convey urgency?"] = [0.92, 0.08],
        ["Which team should handle this?"] = [0.73, 0.27],
        ["How frustrated is the customer?"] = [0.12, 0.7, 0.18],
        ["Which product is this about?"] = [0.55, 0.45],
        ["How much effort will this take?"] = [0.2, 0.6, 0.2],
    };

    // How often each request body was sent, so a body sent again, by a later probe, gets an id of its own. The probes run
    // one after another, so the count is the same every run.
    private readonly Dictionary<string, int> _sent = new(StringComparer.Ordinal);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var text = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var id = Id(text);

        if (request.Headers.Authorization is not { Scheme: "Bearer", Parameter: { } key } || string.Equals(key, ExchangeCapture.InvalidKey, StringComparison.Ordinal))
        {
            return Respond(request, HttpStatusCode.Unauthorized, id, new JsonObject
            {
                ["error"] = new JsonObject { ["message"] = "User not found.", ["code"] = 401 },
            });
        }

        using var body = JsonDocument.Parse(text);
        var prompt = UserText(body.RootElement);
        var question = Question(prompt);
        var probabilities = Script.TryGetValue(question, out var scripted)
            ? scripted
            : throw new InvalidOperationException("The script has no answer for this question; add it to ScriptedChatNetwork.");

        // The labels most likely first, as a provider lists its top logprobs.
        var top = new JsonArray();
        foreach (var (label, probability) in probabilities.Select((p, i) => (((char)('A' + i)).ToString(), p)).OrderByDescending(entry => entry.p))
        {
            top.Add(Token(label, Math.Log(probability)));
        }

        var picked = top[0]!["token"]!.GetValue<string>();
        var first = Token(picked, top[0]!["logprob"]!.GetValue<double>());
        first["top_logprobs"] = top;
        var promptTokens = prompt.Length / 4;

        return Respond(request, HttpStatusCode.OK, id, new JsonObject
        {
            ["id"] = "gen-" + id,
            ["provider"] = "OpenAI",
            ["model"] = body.RootElement.GetProperty("model").GetString(),
            ["object"] = "chat.completion",
            ["created"] = Created,
            ["choices"] = new JsonArray(new JsonObject
            {
                ["index"] = 0,
                ["logprobs"] = new JsonObject { ["content"] = new JsonArray(first), ["refusal"] = null },
                ["finish_reason"] = "length",
                ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = picked, ["refusal"] = null },
            }),
            ["usage"] = new JsonObject { ["prompt_tokens"] = promptTokens, ["completion_tokens"] = 1, ["total_tokens"] = promptTokens + 1 },
        });
    }

    private string Id(string body)
    {
        int count;
        lock (_sent)
        {
            count = _sent[body] = _sent.GetValueOrDefault(body) + 1;
        }

        var bytes = Encoding.UTF8.GetBytes(body + "#" + count.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexStringLower(SHA256.HashData(bytes))[..24];
    }

    private static JsonObject Token(string token, double logprob) => new()
    {
        ["token"] = token,
        ["logprob"] = logprob,
        ["bytes"] = new JsonArray([.. Encoding.UTF8.GetBytes(token).Select(b => JsonValue.Create(b))]),
    };

    private static HttpResponseMessage Respond(HttpRequestMessage request, HttpStatusCode status, string id, JsonObject body)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        response.Headers.Add("x-request-id", "req_" + id);
        return response;
    }

    // The user message's text, sent as a string or as text parts.
    private static string UserText(JsonElement body)
    {
        foreach (var message in body.GetProperty("messages").EnumerateArray())
        {
            if (!string.Equals(message.GetProperty("role").GetString(), "user", StringComparison.Ordinal))
            {
                continue;
            }

            var content = message.GetProperty("content");
            if (content.ValueKind == JsonValueKind.String)
            {
                return content.GetString()!;
            }

            var text = new StringBuilder();
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var value))
                {
                    text.Append(value.GetString());
                }
            }

            return text.ToString();
        }

        throw new InvalidOperationException("The request has no user message.");
    }

    // The bridge's prompt starts with "Question: " and the instructions, up to the end of that line.
    private static string Question(string prompt)
    {
        const string Prefix = "Question: ";
        if (!prompt.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The user message does not start with the question.");
        }

        var end = prompt.IndexOf('\n', StringComparison.Ordinal);
        return end < 0 ? prompt[Prefix.Length..] : prompt[Prefix.Length..end];
    }
}
