using System.Net;
using System.Text;

namespace Minos.Samples.Tests;

/// <summary>Builds a question set from a canned response body, through a real <see cref="DecisionClient"/> over a stub handler.</summary>
internal static class Canned
{
    public static async Task<T> EvaluateAsync<T>(string json)
        where T : IQuestionSet<T>
    {
        using var http = new HttpClient(new Handler(json)) { BaseAddress = new Uri("https://canned.example/api/") };
        using var client = new DecisionClient(http, new DecisionClientOptions { ApiKey = "canned-key", MaxRetries = 0 });
        var result = await client.EvaluateAsync<T>("any message", CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Kind + ": " + result.Error.Message : null);
        return result.Value;
    }

    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
