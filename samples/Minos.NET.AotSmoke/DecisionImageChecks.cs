using System.Text;
using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary>
/// <see cref="DecisionImage"/>'s factories and equality, and the image overloads of <see cref="DecisionClientExtensions"/>
/// over <see cref="ExtensionFallbackClient"/>, under Native AOT.
/// </summary>
internal static class DecisionImageChecks
{
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47];

    [Covers("static Minos.DecisionImage.FromBytes(System.ReadOnlyMemory<byte> data, string! mediaType) -> Minos.DecisionImage")]
    [Covers("static Minos.DecisionImage.FromDataUrl(string! dataUrl) -> Minos.DecisionImage")]
    [Covers("Minos.DecisionImage.DecisionImage() -> void")]
    [Covers("Minos.DecisionImage.Equals(Minos.DecisionImage other) -> bool")]
    public static void ImagesAreCreatedFromBytesAndDataUrlsAndCompared()
    {
        var fromBytes = DecisionImage.FromBytes(PngBytes, "image/png");
        var fromUrl = DecisionImage.FromDataUrl("data:image/png;base64," + Convert.ToBase64String(PngBytes));
        var uninitialized = new DecisionImage();

        Program.Check(
            string.Equals(fromBytes.MediaType, "image/png", StringComparison.Ordinal)
                && fromBytes.Data.Span.SequenceEqual(PngBytes)
                && uninitialized.Data.IsEmpty,
            "DecisionImage.FromBytes keeps its type and bytes, and an uninitialized image has none, under Native AOT");
        Program.Check(
            fromBytes.Equals(fromUrl)
                && !fromBytes.Equals(DecisionImage.FromBytes(PngBytes, "image/gif"))
                && !fromBytes.Equals(uninitialized),
            "DecisionImage.FromDataUrl reads the same image FromBytes creates, and Equals compares type and bytes, under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => DecisionImage.FromBytes(PngBytes, "image/svg+xml"))
                && SmokeAssert.Throws<ArgumentException>(() => DecisionImage.FromBytes(ReadOnlyMemory<byte>.Empty, "image/png"))
                && SmokeAssert.Throws<ArgumentException>(() => DecisionImage.FromDataUrl("https://example.test/a.png")),
            "DecisionImage rejects an unsupported type, empty data and a remote URL under Native AOT");
    }

    [Covers("Minos.DecisionRequest.Equals(Minos.DecisionRequest? other) -> bool")]
    public static void RequestsCompareTheirImagesByValue()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Does this convey urgency?")));
        var state = DecisionContent.FromString(SmokeAnswers.State);
        var request = new DecisionRequest(definition, state) { Images = [DecisionImage.FromBytes(PngBytes, "image/png")] };
        var same = new DecisionRequest(definition, state) { Images = [DecisionImage.FromBytes((byte[])PngBytes.Clone(), "image/png")] };

        Program.Check(
            request.Equals(same)
                && request.GetHashCode() == same.GetHashCode()
                && !request.Equals(new DecisionRequest(definition, state))
                && !request.Equals(new DecisionRequest(definition, state) { Images = [DecisionImage.FromBytes(PngBytes, "image/gif")] })
                && !request.Equals(null),
            "DecisionRequest.Equals compares the images by value under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, string! state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, string! state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T>(this Minos.IDecisionClient! client, System.Text.Json.JsonElement state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateUtf8Async<T>(this Minos.IDecisionClient! client, System.ReadOnlyMemory<byte> utf8JsonState, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateUtf8Async<T>(this Minos.IDecisionClient! client, System.ReadOnlyMemory<byte> utf8JsonState, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedExtensionsSendTheImagesWithTheState()
    {
        var fallback = new ExtensionFallbackClient(await IDecisionClientChecks.ResponseFor(Program.TriageResponse, SmokeTriage.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var state = JsonDocument.Parse(SmokeAnswers.JsonState);
        using var cancellation = new CancellationTokenSource();
        DecisionImage[] images = [DecisionImage.FromBytes(PngBytes, "image/png"), DecisionImage.FromBytes(PngBytes, "image/webp")];
        var text = DecisionContent.FromString(SmokeAnswers.State);
        var json = DecisionContent.FromJson(state.RootElement);
        var utf8 = Encoding.UTF8.GetBytes(SmokeAnswers.JsonState);

        var fromText = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, images).ConfigureAwait(false);
        var sentText = Sent(fallback, text, images, CancellationToken.None);
        var fromCancellableText = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, images, cancellation.Token).ConfigureAwait(false);
        var sentCancellableText = Sent(fallback, text, images, cancellation.Token);
        var fromJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement, images).ConfigureAwait(false);
        var sentJson = Sent(fallback, json, images, CancellationToken.None);
        var fromCancellableJson = await client.EvaluateAsync<SmokeTriage>(state.RootElement, images, cancellation.Token).ConfigureAwait(false);
        var sentCancellableJson = Sent(fallback, json, images, cancellation.Token);
        var fromUtf8 = await client.EvaluateUtf8Async<SmokeTriage>(utf8, images).ConfigureAwait(false);
        var sentUtf8 = Sent(fallback, json, images, CancellationToken.None);
        var fromCancellableUtf8 = await client.EvaluateUtf8Async<SmokeTriage>(utf8, images, cancellation.Token).ConfigureAwait(false);
        var sentCancellableUtf8 = Sent(fallback, json, images, cancellation.Token);
        var noImages = await client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, []).ConfigureAwait(false);

        Program.Check(
            sentText && sentCancellableText && sentJson && sentCancellableJson && sentUtf8 && sentCancellableUtf8,
            "the typed image extensions send the images in order beside the same state, with the caller's token, under Native AOT");
        Program.Check(
            SmokeAnswers.IsTriage(fromText)
                && SmokeAnswers.IsTriage(fromCancellableText)
                && SmokeAnswers.IsTriage(fromJson)
                && SmokeAnswers.IsTriage(fromCancellableJson)
                && SmokeAnswers.IsTriage(fromUtf8)
                && SmokeAnswers.IsTriage(fromCancellableUtf8)
                && SmokeAnswers.IsTriage(noImages)
                && fallback.LastRequest is { Images.Count: 0 },
            "the typed image extensions create typed answers, and an empty list sends no images, under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentNullException>(() => client.EvaluateAsync<SmokeTriage>(SmokeAnswers.State, null!).AsTask()).ConfigureAwait(false),
            "the typed image extensions reject null images under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T, TState>(this Minos.IDecisionClient! client, TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync<T, TState>(this Minos.IDecisionClient! client, TState state, System.Text.Json.Serialization.Metadata.JsonTypeInfo<TState>! stateTypeInfo, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<T, Minos.DecisionError!>>")]
    public static async Task TypedStateExtensionsSendTheImagesWithTheState()
    {
        var fallback = new ExtensionFallbackClient(await IDecisionClientChecks.ResponseFor(Program.CredentialsResponse, SmokeStateTriage.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var cancellation = new CancellationTokenSource();
        DecisionImage[] images = [DecisionImage.FromBytes(PngBytes, "image/gif")];
        var state = new SmokeState("Payouts failing", SmokeAnswers.State);
        var expected = DecisionContent.FromValue(state, SmokeStateJsonContext.Default.SmokeState);

        var result = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, images)
            .ConfigureAwait(false);
        var sent = Sent(fallback, expected, images, CancellationToken.None);
        var cancellable = await client
            .EvaluateAsync<SmokeStateTriage, SmokeState>(state, SmokeStateJsonContext.Default.SmokeState, images, cancellation.Token)
            .ConfigureAwait(false);
        var sentCancellable = Sent(fallback, expected, images, cancellation.Token);

        Program.Check(
            sent && sentCancellable,
            "the EvaluateAsync<T, TState> image extensions send the images beside the serialized state under Native AOT");
        Program.Check(
            result.IsSuccess && !result.Value.RequestsCredentials.Value && cancellable.IsSuccess && !cancellable.Value.RequestsCredentials.Value,
            "the EvaluateAsync<T, TState> image extensions create typed answers under Native AOT");
    }

    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync(this Minos.IDecisionClient! client, Minos.QuestionSet! questionSet, Minos.DecisionContent state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    [Covers("static Minos.DecisionClientExtensions.EvaluateAsync(this Minos.IDecisionClient! client, Minos.QuestionSet! questionSet, Minos.DecisionContent state, System.Collections.Generic.IReadOnlyList<Minos.DecisionImage>! images, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<ZeroAlloc.Results.Result<Minos.Answers!, Minos.DecisionError!>>")]
    public static async Task BuiltSetExtensionsSendTheImagesWithTheState()
    {
        var set = SmokeBuiltSet.Full(out var credentials, out _, out _, out _);
        var fallback = new ExtensionFallbackClient(await IDecisionClientChecks.ResponseFor(SmokeBuiltSet.ResponseJson, set.Definition).ConfigureAwait(false));
        IDecisionClient client = fallback;
        using var cancellation = new CancellationTokenSource();
        DecisionImage[] images = [DecisionImage.FromBytes(PngBytes, "image/jpeg")];
        var expected = DecisionContent.FromString(SmokeAnswers.State);

        var result = await client.EvaluateAsync(set, expected, images).ConfigureAwait(false);
        var sent = Sent(fallback, expected, images, CancellationToken.None);
        var cancellable = await client.EvaluateAsync(set, expected, images, cancellation.Token).ConfigureAwait(false);
        var sentCancellable = Sent(fallback, expected, images, cancellation.Token);

        Program.Check(
            sent && sentCancellable,
            "the built-set image extensions send the images beside the state under Native AOT");
        Program.Check(
            result.IsSuccess && cancellable.IsSuccess && !result.Value.Get(credentials).Value,
            "the built-set image extensions read the answers under Native AOT");
        Program.Check(
            await SmokeAssert.ThrowsAsync<ArgumentException>(() => client.EvaluateAsync(set, default, images).AsTask()).ConfigureAwait(false),
            "the built-set image extension rejects uninitialized content under Native AOT");
    }

    // Whether the last request the fallback client received carried expected as its state, the images in order, no model, and token.
    private static bool Sent(ExtensionFallbackClient fallback, DecisionContent expected, DecisionImage[] images, CancellationToken token)
        => fallback.LastRequest is { Model: null } request
            && request.State.Equals(expected)
            && request.Images.Count == images.Length
            && request.Images.Zip(images).All(pair => pair.First.Equals(pair.Second))
            && fallback.LastToken == token;
}
