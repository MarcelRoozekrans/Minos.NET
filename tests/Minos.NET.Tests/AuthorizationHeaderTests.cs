using System.Net;
using System.Text.Json;
using Minos.SystemOne;
using Minos.SystemOne.Serialization;

namespace Minos.Tests;

/// <summary>What a real <see cref="SystemOneClient"/> sends for each preset: the <c>Authorization</c> header and the URL.</summary>
public sealed class AuthorizationHeaderTests : IDisposable
{
    private readonly List<HttpClient> _httpClients = [];

    [Fact]
    public async Task Custom_without_a_key_sends_no_authorization_header()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: static options =>
        {
            options.Provider = SystemOneProvider.Custom;
            options.ApiKey = null;
            options.BaseAddress = new Uri("http://localhost:8000/");
        });

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsSuccess);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Null(sent.Authorization);
        Assert.Equal(new Uri("http://localhost:8000/v1/systemone"), sent.Uri);
    }

    [Fact]
    public async Task Custom_without_a_key_sends_no_authorization_header_on_the_raw_calls()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: static options =>
        {
            options.Provider = SystemOneProvider.Custom;
            options.ApiKey = null;
            options.BaseAddress = new Uri("http://localhost:8000/");
            options.Path = "decide/v1/systemone";
        });

        var result = await client.EvaluateAsync(NoulRequest(model: "m"));

        Assert.True(result.IsSuccess);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Null(sent.Authorization);
        Assert.Equal(new Uri("http://localhost:8000/decide/v1/systemone"), sent.Uri);
    }

    [Fact]
    public async Task TypeSafe_sends_its_bearer_key()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: static options => options.ApiKey = "k");

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Bearer k", ClientTestKit.OnlyRequest(handler).Authorization);
    }

    [Fact]
    public async Task Cloudflare_sends_a_typed_evaluation_to_the_model_url()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: Cloudflare);

        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket"));

        Assert.True(result.IsSuccess);
        var sent = ClientTestKit.OnlyRequest(handler);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/abc123/ai/run/@cf/cloudflare/clef-flash", sent.Uri!.AbsoluteUri);
        Assert.Equal("Bearer test-key", sent.Authorization);
        Assert.Contains("\"model\":\"clef-flash\"", sent.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cloudflare_sends_a_raw_evaluation_to_its_own_model_url()
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: Cloudflare);

        var result = await client.EvaluateAsync(NoulRequest(model: "clef"));

        Assert.True(result.IsSuccess);
        Assert.Equal("https://api.cloudflare.com/client/v4/accounts/abc123/ai/run/@cf/cloudflare/clef", ClientTestKit.OnlyRequest(handler).Uri!.AbsoluteUri);
    }

    [Theory]
    [InlineData("@cf/x")]
    [InlineData("..")]
    [InlineData("a b")]
    public async Task Cloudflare_rejects_a_request_model_that_is_not_a_single_name_without_sending(string model)
    {
        var handler = StubHandler.Json(HttpStatusCode.OK, Fixture.Text("response-noul.json"));
        using var client = ClientTestKit.Client(_httpClients, handler, configure: Cloudflare);

        var typed = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "a ticket") { Model = model });
        var raw = await client.EvaluateAsync(NoulRequest(model));

        Assert.Equal(DecisionErrorKind.Validation, typed.Error.Kind);
        Assert.Equal("A Cloudflare model must be a single name such as clef-flash.", typed.Error.Message);
        Assert.Equal(DecisionErrorKind.Validation, raw.Error.Kind);
        Assert.Equal("A Cloudflare model must be a single name such as clef-flash.", raw.Error.Message);
        Assert.Empty(handler.Requests);
    }

    public void Dispose()
    {
        for (var i = 0; i < _httpClients.Count; i++)
        {
            _httpClients[i].Dispose();
        }
    }

    private static void Cloudflare(SystemOneClientOptions options)
    {
        options.Provider = SystemOneProvider.CloudflareWorkersAi;
        options.AccountId = "abc123";
        options.Model = "clef-flash";
    }

    private static SystemOneRequest NoulRequest(string model)
    {
        var request = JsonSerializer.Deserialize(Fixture.Text("request-noul.json"), DecisionJsonContext.Default.SystemOneRequest)!;
        return new SystemOneRequest { State = request.State, Questions = request.Questions, Model = model };
    }
}
