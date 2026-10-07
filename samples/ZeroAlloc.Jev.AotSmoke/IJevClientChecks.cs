using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// <see cref="IJevClient"/>'s members called through the interface. The default interface methods run on
/// <see cref="DimFallbackClient"/>, which implements only the two abstract members, so each check proves the
/// compatible fallback path; the abstract members run on a <see cref="JevClient"/> resolved as an <see cref="IJevClient"/>.
/// </summary>
internal static class IJevClientChecks
{
    private const string State = "Help! My payouts have been failing for 3 days.";
    private const string JsonState = """{"subject":"Payouts failing","body":"Help! My payouts have been failing for 3 days."}""";

    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync(ZeroAlloc.Jev.SystemOneRequest! request, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<ZeroAlloc.Jev.SystemOneResponse!, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.ListModelsAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<ZeroAlloc.Jev.ModelList!, ZeroAlloc.Jev.JevError!>>")]
    public static async Task AbstractMembersRunThroughTheInterface()
    {
        var client = ResolveOver(Program.NoulResponse, out var provider);
        var modelsClient = ResolveOver(Program.ModelsResponse, out var modelsProvider);
        using var cancellation = new CancellationTokenSource();

        var evaluated = await client.EvaluateAsync(Program.Request(), cancellation.Token).ConfigureAwait(false);
        var models = await modelsClient.ListModelsAsync(cancellation.Token).ConfigureAwait(false);
        await provider.DisposeAsync().ConfigureAwait(false);
        await modelsProvider.DisposeAsync().ConfigureAwait(false);

        Program.Check(
            evaluated.IsSuccess && evaluated.Value.Answers["is_urgent"] is NoulAnswer { Noul: 0.95 },
            "IJevClient.EvaluateAsync(request, cancellationToken) evaluates through the interface under Native AOT");
        Program.Check(
            models.IsSuccess && string.Equals(models.Value.Models[0].Name, "jev-latest", StringComparison.Ordinal),
            "IJevClient.ListModelsAsync lists the models through the interface under Native AOT");
    }

    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync<T>(string! state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync<T>(System.Text.Json.JsonElement state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync<T>(System.Text.Json.JsonElement state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateUtf8Async<T>(System.ReadOnlyMemory<byte> utf8JsonState, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken)) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    public static async Task TypedDefaultMethodsParseAnswers()
    {
        IJevClient client = new DimFallbackClient(await ResponseFor(Program.TriageResponse).ConfigureAwait(false));
        using var state = JsonDocument.Parse(JsonState);
        using var cancellation = new CancellationTokenSource();

        var fromText = await client.EvaluateAsync<SmokeTriage>(State, cancellation.Token).ConfigureAwait(false);
        var fromJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement).ConfigureAwait(false);
        var fromCancellableJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement, cancellation.Token).ConfigureAwait(false);
        var fromUtf8 = await client.EvaluateUtf8Async<SmokeTriage>(Encoding.UTF8.GetBytes(JsonState)).ConfigureAwait(false);

        Program.Check(
            JevClientChecks.IsTriage(fromText)
                && JevClientChecks.IsTriage(fromJson)
                && JevClientChecks.IsTriage(fromCancellableJson)
                && JevClientChecks.IsTriage(fromUtf8),
            "IJevClient's typed default methods over text, JSON and UTF-8 states parse answers under Native AOT");
    }

    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync<T, TState>(TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync<T, TState>(TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, ZeroAlloc.Jev.JevError!>>")]
    public static async Task TypedStateDefaultMethodsParseAnswers()
    {
        IJevClient client = new DimFallbackClient(await ResponseFor(Program.CredentialsResponse).ConfigureAwait(false));
        using var cancellation = new CancellationTokenSource();
        var state = new SmokeState("Payouts failing", State);

        var result = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState)
            .ConfigureAwait(false);
        var cancellable = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, cancellation.Token)
            .ConfigureAwait(false);

        Program.Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value && cancellable.IsSuccess && !cancellable.Value.RequestsCredentials.Value,
            "IJevClient's EvaluateAsync<T, TState> default methods parse typed answers under Native AOT");
    }

    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync(ZeroAlloc.Jev.JevQuestionSet! questionSet, ZeroAlloc.Jev.JevContent state) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<ZeroAlloc.Jev.JevAnswers!, ZeroAlloc.Jev.JevError!>>")]
    [Covers("ZeroAlloc.Jev.IJevClient.EvaluateAsync(ZeroAlloc.Jev.JevQuestionSet! questionSet, ZeroAlloc.Jev.JevContent state, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<ZeroAlloc.Jev.JevAnswers!, ZeroAlloc.Jev.JevError!>>")]
    public static async Task BuiltSetDefaultMethodsReadAnswers()
    {
        IJevClient client = new DimFallbackClient(await ResponseFor(SmokeBuiltSet.ResponseJson).ConfigureAwait(false));
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        using var cancellation = new CancellationTokenSource();

        var result = await client.EvaluateAsync(set, State).ConfigureAwait(false);
        var cancellable = await client.EvaluateAsync(set, State, cancellation.Token).ConfigureAwait(false);

        Program.Check(
            result.IsSuccess
                && cancellable.IsSuccess
                && !result.Value.Get(credentials).Value
                && result.Value.Get(team).Value == Team.Account
                && string.Equals(cancellable.Value.Get(product).Value, "pro-plan", StringComparison.Ordinal)
                && cancellable.Value.Get(urgency).Value == Urgency.High,
            "IJevClient's built-set default methods read the answers under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateAsync(set, default(JevContent)).AsTask()).ConfigureAwait(false),
            "IJevClient's built-set default method rejects uninitialized content under Native AOT");
    }

    // A client resolved as the interface, as an app gets it, so each call binds to IJevClient's member.
    private static IJevClient ResolveOver(string responseJson, out ServiceProvider provider)
    {
        var services = new ServiceCollection();
        services
            .AddJevClient(options =>
            {
                options.ApiKey = "smoke-key";
                options.BaseAddress = new Uri("https://example.test/api/");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new CannedHandler(HttpStatusCode.OK, responseJson));
        provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IJevClient>();
    }

    // A response parsed from canned JSON by a real client, so the fallback client answers as the service would.
    private static async Task<SystemOneResponse> ResponseFor(string responseJson)
    {
        using var http = Program.Http(HttpStatusCode.OK, responseJson);
        using var client = new JevClient(http, Program.Options());
        var result = await client.EvaluateAsync(Program.Request()).ConfigureAwait(false);
        return result.IsSuccess ? result.Value : throw new InvalidOperationException("The canned response did not parse: " + result.Error.Message);
    }
}
