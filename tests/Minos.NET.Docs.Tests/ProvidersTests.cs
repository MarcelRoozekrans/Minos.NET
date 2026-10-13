using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos.OpenAI;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

// Serialized with the other test classes that set or read the key environment variables.
[Collection("key environment")]
public sealed class ProvidersTests
{
    private const string OpenAIUrgent = """{"model":"gpt-6-luna","answers":[{"type":"predicate","name":"is_urgent","probability":0.91}]}""";

    private const string OpenAIRefusal = """{"model":"gpt-6-luna","answers":[{"type":"refusal","name":"is_urgent"}]}""";

    private const string SystemOneNotDamaged = """
        {
          "model": "local-model",
          "answers": { "is_damaged": { "type": "noul", "noul": 0.2 } },
          "usage": { "input_tokens": 300, "output_tokens": 3 }
        }
        """;

    // A one-pixel image's bytes are not checked; only the media type and a non-empty payload are.
    private const string ScreenshotDataUrl = "data:image/png;base64,iVBORw0KGgo=";

    [Fact]
    public void TheOpenAIClient_IsTheOpenAIProvider_WithLunaByDefault()
    {
        using var client = OpenAIClients.Create("key");

        var metadata = client.GetService<DecisionClientMetadata>();

        Assert.NotNull(metadata);
        Assert.Equal("openai", metadata.ProviderName);
        Assert.Equal(OpenAIDefaults.Model, metadata.DefaultModel);
        Assert.Equal("gpt-6-luna", OpenAIDefaults.Model);
        Assert.Equal(new Uri("https://api.openai.com/v1/"), OpenAIDefaults.BaseAddress);
        Assert.Null(client.GetService<SystemOneDecisionClient>());
    }

    [Fact]
    public void TheOpenAIOptionsTable_ListsEveryPropertyOfTheOptions()
    {
        var rows = PageTables.Rows("providers.md", "OpenAI");
        var properties = typeof(OpenAIDecisionClientOptions).GetProperties().Select(property => property.Name).ToArray();

        Assert.Equal(
            properties.Order(StringComparer.Ordinal),
            rows.SelectMany(row => PageTables.AllCode(row[0])).Order(StringComparer.Ordinal));
        Assert.Equal(10, properties.Length);
        Assert.Contains("`OpenAIDecisionClientOptions` has ten properties", PageTables.Text("providers.md"), StringComparison.Ordinal);
        Assert.Equal(OpenAIDefaults.ApiKeyEnvironmentVariable, PageTables.AllCode(rows[0][1])[0]);
        Assert.Equal(OpenAIDefaults.BaseAddressEnvironmentVariable, PageTables.AllCode(rows[1][1])[0]);
        Assert.Equal(OpenAIDefaults.BaseAddress.AbsoluteUri, PageTables.AllCode(rows[1][1])[1]);
        Assert.Equal(OpenAIDefaults.Model, PageTables.Code(rows[2][1]));
    }

    // A 503 is retried, and the retry is sent exactly as the first attempt: OpenAI has no retry-count header.
    [Fact]
    public async Task TheOpenAIClient_SendsAPredicate_RetriesWithoutARetryCount_AndReadsItsProbability()
    {
        var (http, client, handler) = OpenAI(Reply.Error(503), Reply.Ok(OpenAIUrgent));
        using (http)
        using (handler)
        using (client)
        {
            Assert.Equal("urgent", await OpenAIClients.TriageAsync(client, "Help!", CancellationToken.None));
        }

        Assert.Equal(2, handler.Requests.Count);
        var (first, retry) = (handler.Requests[0], handler.Requests[1]);
        Assert.Equal("https://docs.example/v1/decisions", retry.Uri!.AbsoluteUri);
        Assert.Equal("Bearer docs-key", retry.Authorization);
        Assert.Null(retry.RetryCount);
        Assert.Equal(first.Body, retry.Body);
        Assert.Contains("\"input\":\"Help!\"", retry.Body, StringComparison.Ordinal);
        Assert.Contains("\"type\":\"predicate\"", retry.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusal_IsRefused_WithTheRefusedQuestions_AndIsNotRetried()
    {
        var (http, client, handler) = OpenAI(Reply.Ok(OpenAIRefusal));
        using (http)
        using (handler)
        using (client)
        {
            var result = await client.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Refused, result.Error.Kind);
            Assert.Equal(200, result.Error.StatusCode);
            Assert.Equal(["is_urgent"], OpenAIRefusals.RefusedQuestions(result.Error));
            Assert.All(result.Error.Failures, failure => Assert.Equal("refusal", failure.Rule));
            Assert.Equal(
                "The model refused to answer: is_urgent. Rephrase the questions or the state.",
                ClientFailures.Describe(result.Error));
        }

        Assert.Collection(handler.Requests, _ => { });
        Assert.Empty(OpenAIRefusals.RefusedQuestions(new DecisionError(DecisionErrorKind.Network, "down")));
    }

    [Fact]
    public async Task OpenAI_RefusesNoulMeanings_WithMin204_AndCriterionDetail_WithMin205_BeforeSending()
    {
        var set = QuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _, noul => noul.WhenTrue("The sender needs an answer today"))
            .Choice("team", "Which team?", out _, choice => choice
                .Option("billing", Criterion.Text("Payments").WithExamples("My invoice is wrong"))
                .Option("technical", "Bugs"))
            .Build();
        Assert.True(set.IsSuccess);
        var (http, client, handler) = OpenAI(Reply.Ok(OpenAIUrgent));
        using (http)
        using (handler)
        using (client)
        {
            var result = await client.EvaluateAsync(set.Value, "Help!", CancellationToken.None);

            Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
            Assert.Equal(["MIN204", "MIN205"], result.Error.Failures.Select(failure => failure.Rule));
            Assert.Equal(["is_urgent", "team"], result.Error.Failures.Select(failure => failure.QuestionKey));
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Images_AreSentInTheState_WhereTheProviderTakesThem()
    {
        var options = ProviderOptions.Custom();
        options.Capabilities = new DecisionCapabilities { SupportsImages = true };
        var (http, client, handler) = SystemOne(options, Reply.Ok(SystemOneNotDamaged));
        using (http)
        using (handler)
        using (client)
        {
            Assert.Equal(
                "not damaged",
                await ImageCalls.CheckReturnAsync(client, new byte[] { 0xFF, 0xD8, 0xFF }, ScreenshotDataUrl, CancellationToken.None));
        }

        Assert.Collection(
            handler.Requests,
            sent =>
            {
                Assert.Contains("\"state\":[{\"type\":\"text\",\"text\":\"A return request for a kettle.\"}", sent.Body, StringComparison.Ordinal);
                Assert.Contains("{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/jpeg;base64,/9j/\"}}", sent.Body, StringComparison.Ordinal);
                Assert.Contains(ScreenshotDataUrl, sent.Body, StringComparison.Ordinal);
            });
    }

    [Fact]
    public async Task Images_ToAProviderWithoutThem_AreMin206_AndNothingIsSent()
    {
        var (http, client, handler) = SystemOne(ProviderOptions.Custom(), Reply.Ok(SystemOneNotDamaged));
        using (http)
        using (handler)
        using (client)
        {
            var message = await ImageCalls.CheckReturnAsync(client, new byte[] { 0xFF, 0xD8, 0xFF }, ScreenshotDataUrl, CancellationToken.None);

            Assert.Equal("The provider cannot do that: Custom does not support images.", message);
        }

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void DecisionImage_TakesTheFourImageTypes_AndDataUrlsOnly()
    {
        foreach (var type in new[] { "image/png", "image/jpeg", "image/webp", "image/gif" })
        {
            Assert.Equal(type, DecisionImage.FromBytes(new byte[] { 1 }, type).MediaType);
        }

        Assert.Throws<ArgumentException>(() => DecisionImage.FromBytes(new byte[] { 1 }, "image/bmp"));
        Assert.Throws<ArgumentException>(() => DecisionImage.FromBytes(ReadOnlyMemory<byte>.Empty, "image/png"));
        Assert.Throws<ArgumentException>(() => DecisionImage.FromDataUrl("https://example.com/photo.png"));
    }

    [Fact]
    public void TheOpenRouterModelOptions_AreValid_AndNameTheModels()
    {
        var perplexity = OpenRouterModels.PerplexityWithImages();
        var clef = OpenRouterModels.Clef();
        var luna = OpenRouterModels.Luna();

        Assert.True(perplexity.Capabilities.SupportsImages);
        Assert.Equal("perplexity/pplx-decider-v1.1-27b", perplexity.Model);
        Assert.Equal("cloudflare/clef-flash", clef.Model);
        Assert.Equal("openai/gpt-6-luna-decisions", luna.Model);
        foreach (var options in new[] { perplexity, clef, luna })
        {
            Assert.Equal(SystemOneProvider.OpenRouter, options.Provider);
            options.ApiKey = "key";
            Assert.Null(ClientOptions.Problem(options));
        }

        Assert.Null(clef.Capabilities.SupportsImages);
    }

    [Fact]
    public async Task BothProviders_AreRegistered_TypeSafeByDefault_AndOpenAIUnderItsKey()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["TypeSafe:ApiKey"] = "typesafe-key",
            ["OpenAI:ApiKey"] = "openai-key",
        });
        using var handler = new ScriptedDecision.Handler([Reply.Ok(OpenAIUrgent)]);
        OpenAIRegistration.AddBothProviders(builder).HttpClient.ConfigurePrimaryHttpMessageHandler(() => handler);
        using var host = builder.Build();
        await host.StartAsync();

        var openAI = host.Services.GetRequiredKeyedService<IDecisionClient>("openai");
        var result = await openAI.EvaluateAsync<InboxCheck>("Help!", CancellationToken.None);

        Assert.True(result.Value.IsUrgent.Value);
        Assert.Equal("typesafe", host.Services.GetRequiredService<IDecisionClient>().GetService<DecisionClientMetadata>()!.ProviderName);
        Assert.Equal("openai", openAI.GetService<DecisionClientMetadata>()!.ProviderName);
        Assert.Collection(
            handler.Requests,
            sent =>
            {
                Assert.Equal(new Uri("https://api.openai.com/v1/decisions"), sent.Uri);
                Assert.Equal("Bearer openai-key", sent.Authorization);
            });
        await host.StopAsync();
    }

    private static (HttpClient Http, OpenAIDecisionClient Client, ScriptedDecision.Handler Handler) OpenAI(params Reply[] script)
    {
        var handler = new ScriptedDecision.Handler(script);
        var http = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("https://docs.example/v1/") };
        var options = new OpenAIDecisionClientOptions { ApiKey = "docs-key", MaxRetries = 2, InitialBackoff = TimeSpan.FromMilliseconds(1), MaxRetryDelay = TimeSpan.FromMilliseconds(5) };
        OpenAIDecisionClient.ConfigureHttpClient(http, options);
        return (http, new OpenAIDecisionClient(http, options), handler);
    }

    private static (HttpClient Http, SystemOneDecisionClient Client, ScriptedDecision.Handler Handler) SystemOne(SystemOneDecisionClientOptions options, params Reply[] script)
    {
        var handler = new ScriptedDecision.Handler(script);
        var http = new HttpClient(handler, disposeHandler: false);
        options.MaxRetries = 0;
        SystemOneDecisionClient.ConfigureHttpClient(http, options);
        return (http, new SystemOneDecisionClient(http, options), handler);
    }
}
