using System.Text.Json;
using ZeroAlloc.Jev.Serialization;
using ZeroAlloc.Jev.Transport;
using ZeroAlloc.Results;

namespace ZeroAlloc.Jev.Tests;

/// <summary>What the <see cref="JevClient"/> test classes share: a client over a stub handler and a fake client.</summary>
internal static class ClientTestKit
{
    public const string TestModel = "jev-test-model";

    // The exception must come from the call itself, before any task exists, as for the default interface methods.
    public static TException ThrowsSynchronously<TException>(Func<Task> call)
        where TException : Exception
    {
        Task? task = null;
        var exception = Record.Exception(() => { task = call(); });
        Assert.Null(task);
        return Assert.IsType<TException>(exception);
    }

    public static StubHandler.Captured OnlyRequest(StubHandler handler)
    {
        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        return Assert.Single(handler.Requests);
#pragma warning restore HLQ005
    }

    /// <summary>Creates a client over <paramref name="handler"/>; the caller disposes <paramref name="httpClients"/>.</summary>
    public static JevClient Client(
        List<HttpClient> httpClients, StubHandler handler, CountingPool? pool = null, JevProvider provider = JevProvider.TypeSafe)
    {
        var http = new HttpClient(handler);
        httpClients.Add(http);
        var settings = JevClientSettings.Resolve(
            new JevClientOptions { ApiKey = "test-key", Provider = provider, Model = TestModel, MaxRetries = 0 },
            _ => null);
        return new JevClient(settings, http, ownedHandler: null, TimeProvider.System, pool ?? new CountingPool());
    }

    /// <summary>
    /// Implements only the abstract members, so typed and built-set calls run the default interface methods. Answers
    /// with <paramref name="responseJson"/>, or the <c>response-noul.json</c> fixture.
    /// </summary>
    public sealed class CapturingClient(string? responseJson = null) : IJevClient
    {
        private readonly List<SystemOneRequest> _requests = [];

        public SystemOneRequest OnlyRequest()
        {
            // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
            return Assert.Single(_requests);
#pragma warning restore HLQ005
        }

        public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken cancellationToken)
        {
            _requests.Add(request);
            return ValueTask.FromResult(Result<SystemOneResponse, JevError>.Success(
                JsonSerializer.Deserialize(responseJson ?? Fixture.Text("response-noul.json"), JevJsonContext.Default.SystemOneResponse)!));
        }

        public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
