using System.Net;
using System.Text;

namespace ZeroAlloc.Jev.Docs.Tests;

/// <summary>Evaluates a question set against a canned response, through a real <see cref="JevClient"/>.</summary>
internal static class CannedJev
{
    /// <summary>Sends <paramref name="state"/> and returns the typed answers parsed from <paramref name="responseJson"/>.</summary>
    public static async Task<T> EvaluateAsync<T>(string responseJson, string state)
        where T : IJevQuestionSet<T>
    {
        using var http = new HttpClient(new Handler(responseJson)) { BaseAddress = new Uri("https://docs.example/api/") };
        using var jev = new JevClient(http, new JevClientOptions { ApiKey = "docs-key", MaxRetries = 0 });
        var result = await jev.EvaluateAsync<T>(state, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    /// <summary>A client over a canned response, for snippets that take an <see cref="IJevClient"/>; the caller disposes both.</summary>
    public static (HttpClient Http, JevClient Jev) Client(string responseJson)
    {
        var http = new HttpClient(new Handler(responseJson)) { BaseAddress = new Uri("https://docs.example/api/") };
        return (http, new JevClient(http, new JevClientOptions { ApiKey = "docs-key", MaxRetries = 0 }));
    }

    private sealed class Handler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            });
    }
}
