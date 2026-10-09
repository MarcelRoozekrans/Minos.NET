using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="JevClient"/>'s constructors, <see cref="JevClient.Dispose"/> and the evaluation overloads the other
/// checks leave out, each called on a <see cref="JevClient"/> under Native AOT.
/// </summary>
internal static class JevClientChecks
{

    [Covers("Minos.JevClient.JevClient() -> void")]
    [Covers("Minos.JevClient.JevClient(Minos.JevClientOptions? options) -> void")]
    [Covers("Minos.JevClient.JevClient(Minos.JevClientOptions? options, Microsoft.Extensions.Logging.ILoggerFactory? loggerFactory) -> void")]
    [Covers("Minos.JevClient.Dispose() -> void")]
    public static async Task ClientsThatOwnTheirHttpClientRefuseCallsAfterDispose()
    {
        using (SmokeAssert.NoApiKeyEnvironment())
        {
            Program.Check(
                SmokeAssert.Throws<InvalidOperationException>(() => new JevClient().Dispose()),
                "new JevClient() without TYPESAFE_API_KEY throws under Native AOT");
        }

        JevClient fromEnvironment;
        using (SmokeAssert.TypeSafeEnvironment())
        {
            fromEnvironment = new JevClient();
        }

        var fromOptions = new JevClient(Program.Options());
        var withLogging = new JevClient(Program.Options(), NullLoggerFactory.Instance);
        fromEnvironment.Dispose();
        fromOptions.Dispose();
        withLogging.Dispose();
        withLogging.Dispose();

        Program.Check(
            await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromEnvironment.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => fromOptions.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false)
                && await SmokeAssert.ThrowsAsync<ObjectDisposedException>(() => withLogging.EvaluateAsync(Program.Request()).AsTask()).ConfigureAwait(false),
            "a JevClient that owns its HttpClient is built from the environment or options, disposes twice and then refuses calls, under Native AOT");
    }

    [Covers("Minos.JevClient.JevClient(System.Net.Http.HttpClient! httpClient) -> void")]
    public static async Task ClientOverAnHttpClientReadsTheKeyFromTheEnvironment()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.NoulResponse);
        JevClient client;
        using (SmokeAssert.TypeSafeEnvironment())
        {
            client = new JevClient(http);
        }

        using (client)
        {
            var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);

            Program.Check(
                result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 },
                "new JevClient(httpClient) takes its key from TYPESAFE_API_KEY and evaluates under Native AOT");
        }
    }

    [Covers("Minos.JevClient.EvaluateAsync(Minos.JevQuestionSet! questionSet, Minos.JevContent state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.JevAnswers!, Minos.JevError!>>")]
    public static async Task BuiltSetEvaluatesWithACancellationToken()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        using var http = Program.Http(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson);
        using var client = new JevClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync(set, SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(result.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && result.Value.Get(urgency).Value == Urgency.High,
            "JevClient.EvaluateAsync(set, state, cancellationToken) evaluates a built set under Native AOT");
    }

    [Covers("Minos.JevClient.EvaluateAsync<T>(string! state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.JevError!>>")]
    public static async Task TypedTextStateEvaluatesWithACancellationToken()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new JevClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, cancellation.Token).ConfigureAwait(false);

        Program.Check(SmokeAnswers.IsTriage(result), "JevClient.EvaluateAsync<T>(string, cancellationToken) parses typed answers under Native AOT");
    }

    [Covers("Minos.JevClient.EvaluateAsync<T>(System.Text.Json.JsonElement state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.JevError!>>")]
    [Covers("Minos.JevClient.EvaluateAsync<T>(System.Text.Json.JsonElement state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.JevError!>>")]
    public static async Task TypedJsonStateEvaluates()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new JevClient(http, Program.Options());
        using var state = JsonDocument.Parse(SmokeAnswers.JsonState);
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync<SmokeTriage>(state.RootElement).ConfigureAwait(false);
        var cancellable = await client.EvaluateAsync<SmokeTriage>(state.RootElement, cancellation.Token).ConfigureAwait(false);

        Program.Check(
            SmokeAnswers.IsTriage(result) && SmokeAnswers.IsTriage(cancellable),
            "JevClient.EvaluateAsync<T>(JsonElement) and its cancellable overload parse typed answers under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateAsync<SmokeTriage>(default(JsonElement)).AsTask()).ConfigureAwait(false),
            "JevClient.EvaluateAsync<T>(JsonElement) rejects an undefined JSON state under Native AOT");
    }

    [Covers("Minos.JevClient.EvaluateUtf8Async<T>(System.ReadOnlyMemory<byte> utf8JsonState, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.JevError!>>")]
    public static async Task TypedUtf8StateEvaluates()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.TriageResponse);
        using var client = new JevClient(http, Program.Options());
        var state = Encoding.UTF8.GetBytes(SmokeAnswers.JsonState);

        var result = await client.EvaluateUtf8Async<SmokeTriage>(state).ConfigureAwait(false);

        Program.Check(SmokeAnswers.IsTriage(result), "JevClient.EvaluateUtf8Async<T> parses typed answers from a UTF-8 JSON state under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateUtf8Async<SmokeTriage>("{} {}"u8.ToArray()).AsTask()).ConfigureAwait(false),
            "JevClient.EvaluateUtf8Async<T> rejects a state that is not one JSON value under Native AOT");
    }

    [Covers("Minos.JevClient.EvaluateAsync<T, TState>(TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.JevError!>>")]
    public static async Task TypedStateEvaluatesWithACancellationToken()
    {
        using var http = Program.Http(HttpStatusCode.OK, Program.CredentialsResponse);
        using var client = new JevClient(http, Program.Options());
        using var cancellation = new CancellationTokenSource();
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);

        var result = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, cancellation.Token)
            .ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value,
            "JevClient.EvaluateAsync<T, TState>(state, stateTypeInfo, cancellationToken) parses typed answers under Native AOT");
    }
}
