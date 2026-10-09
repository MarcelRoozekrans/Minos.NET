using System.Net;
using System.Text;

namespace Minos.Docs.Tests;

/// <summary>Evaluates a question set against a canned response, through a real <see cref="DecisionClient"/>.</summary>
internal static class CannedDecision
{
    /// <summary>Sends <paramref name="state"/> and returns the typed answers parsed from <paramref name="responseJson"/>.</summary>
    public static async Task<T> EvaluateAsync<T>(string responseJson, string state)
        where T : IQuestionSet<T>
    {
        using var http = new HttpClient(new Handler(responseJson)) { BaseAddress = new Uri("https://docs.example/api/") };
        using var jev = new DecisionClient(http, new DecisionClientOptions { ApiKey = "docs-key", MaxRetries = 0 });
        var result = await jev.EvaluateAsync<T>(state, CancellationToken.None);
        Assert.True(result.IsSuccess, result.IsFailure ? $"{result.Error.Kind}: {result.Error.Message}" : null);
        return result.Value;
    }

    /// <summary>
    /// A client over a canned response, for snippets that take an <see cref="IDecisionClient"/>; the caller disposes both.
    /// <c>Requests</c> holds the body of every request the client sent, in order.
    /// </summary>
    public static (HttpClient Http, DecisionClient Jev, IReadOnlyList<string> Requests) Client(string responseJson)
    {
        var handler = new Handler(responseJson);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://docs.example/api/") };
        return (http, new DecisionClient(http, new DecisionClientOptions { ApiKey = "docs-key", MaxRetries = 0 }), handler.Requests);
    }

    private sealed class Handler(string responseJson) : HttpMessageHandler
    {
        private readonly List<string> _requests = [];

        public IReadOnlyList<string> Requests => _requests;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requests.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
