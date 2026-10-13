using System.Net;
using System.Text;
using System.Text.Json;
using Minos.SystemOne;

namespace Minos.Tests;

public sealed class SystemOneImageRequestTests : IDisposable
{
    private const string Answer = """{"model":"m","answers":{"is_urgent":{"type":"noul","noul":0.9}}}""";
    private const string Tail = ",\"model\":\"" + ClientTestKit.TestModel + "\",\"questions\":";
    private const string PngPart = """{"type":"image_url","image_url":{"url":"data:image/png;base64,iVBORw=="}}""";

    private readonly List<HttpClient> _httpClients = [];

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    [Fact]
    public async Task TextState_WithOneImage_IsATextPartThenAnImagePart()
    {
        var body = await Send(DecisionContent.FromString("A photo."), [Png]);

        Assert.StartsWith("""{"state":[{"type":"text","text":"A photo."},""" + PngPart + "]" + Tail, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task JsonObjectState_WithTwoImages_HoldsTheRawJsonAsTheText()
    {
        using var document = JsonDocument.Parse("""{"a":1}""");
        var jpeg = DecisionImage.FromBytes(new byte[] { 0xFF, 0xD8 }, "image/jpeg");

        var body = await Send(DecisionContent.FromJson(document.RootElement), [Png, jpeg]);

        Assert.StartsWith(
            """{"state":[{"type":"text","text":"{\u0022a\u0022:1}"},""" + PngPart
            + """,{"type":"image_url","image_url":{"url":"data:image/jpeg;base64,/9g="}}]""" + Tail,
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Utf8JsonStringState_WithOneImage_HoldsTheDecodedString()
    {
        var body = await Send(DecisionContent.FromUtf8Json("\"caf\\u00e9 \\\"q\\\"\""u8), [Png]);

        Assert.StartsWith("""{"state":[{"type":"text","text":"caf\u00E9 \u0022q\u0022"},""" + PngPart + "]" + Tail, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithoutImages_StateIsWrittenAsBefore()
    {
        var body = await Send(DecisionContent.FromString("A photo."), []);

        Assert.StartsWith("{\"state\":\"A photo.\"" + Tail, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WithImages_TheBodyIsValidJsonWithTheDataUrlUnescaped()
    {
        var body = await Send(DecisionContent.FromString("A photo."), [DecisionImage.FromBytes(new byte[] { 0xFB, 0xEF, 0xBE }, "image/webp")]);

        using var document = JsonDocument.Parse(body);
        var parts = document.RootElement.GetProperty("state");
        Assert.Equal("data:image/webp;base64,++++", parts[1].GetProperty("image_url").GetProperty("url").GetString());
        Assert.Contains("base64,++++", body, StringComparison.Ordinal);
        Assert.Equal(ClientTestKit.TestModel, document.RootElement.GetProperty("model").GetString());
    }

    private static DecisionImage Png => DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");

    private async Task<string> Send(DecisionContent state, DecisionImage[] images)
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Answer);
        using var client = ClientTestKit.Client(
            _httpClients,
            handler,
            provider: SystemOneProvider.OpenRouter,
            configure: options => options.Capabilities = new() { SupportsImages = true });

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), state) { Images = images });

        Assert.True(result.IsSuccess);
        return ClientTestKit.OnlyRequest(handler).Body!;
    }
}
