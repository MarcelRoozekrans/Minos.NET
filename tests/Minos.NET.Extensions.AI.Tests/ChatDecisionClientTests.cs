using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

public sealed class ChatDecisionClientTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("I was charged twice.");

    [Fact]
    public async Task NoQuestions_IsValidation_AndSendsNothing()
    {
        var chat = Unanswered();
        using var client = chat.AsDecisionClient();

        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(), State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Validation, result.Error.Kind);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task TooManyOptions_IsUnsupportedMIN203_AndSendsNothing()
    {
        var chat = Unanswered();
        using var client = chat.AsDecisionClient();
        var options = Enumerable.Range(0, 27).Select(i => new OptionDefinition("option_" + i, null)).ToArray();
        var definition = new QuestionSetDefinition(QuestionDefinition.Choice("team", "Which team handles it?", options));

        var result = await client.EvaluateAsync(new DecisionRequest(definition, State));

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal("MIN203", result.Error.Failures[0].Rule);
        Assert.Equal("team", result.Error.Failures[0].QuestionKey);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task Images_AreUnsupportedByDefault_MIN206()
    {
        var chat = Unanswered();
        using var client = chat.AsDecisionClient();
        var request = new DecisionRequest(new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Does the customer ask for a refund?")), State)
        {
            Images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")],
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(["MIN206"], result.Error.Failures.Select(failure => failure.Rule));
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public void GetService_ReturnsCapabilities()
    {
        using var client = Unanswered().AsDecisionClient();

        var capabilities = client.GetService<DecisionCapabilities>();

        Assert.NotNull(capabilities);
        Assert.Equal(QuestionKinds.All, capabilities.QuestionKinds);
        Assert.Null(capabilities.MaxQuestions);
        Assert.Equal(26, capabilities.MaxChoiceOptions);
        Assert.False(capabilities.SupportsImages);
        Assert.False(capabilities.SupportsModelListing);
    }

    [Fact]
    public void GetService_ReturnsCapabilities_WithTheOptionsOverrides()
    {
        using var client = Unanswered().AsDecisionClient(new ChatDecisionClientOptions
        {
            Capabilities = new DecisionCapabilities { MaxChoiceOptions = 5, SupportsImages = true, SupportsModelListing = true },
        });

        var capabilities = client.GetService<DecisionCapabilities>()!;

        Assert.Equal(5, capabilities.MaxChoiceOptions);
        Assert.True(capabilities.SupportsImages);
        // The bridge has no model listing, whatever the options say.
        Assert.False(capabilities.SupportsModelListing);
    }

    [Fact]
    public void Options_WithMoreChoiceOptionsThanLabels_AreRejected()
    {
        var chat = Unanswered();

        var exception = Assert.Throws<ArgumentException>(() => chat.AsDecisionClient(new ChatDecisionClientOptions
        {
            Capabilities = new DecisionCapabilities { MaxChoiceOptions = 27 },
        }));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Options_WithABlankModel_AreRejected()
    {
        var exception = Assert.Throws<ArgumentException>(() => Unanswered().AsDecisionClient(new ChatDecisionClientOptions { Model = " " }));

        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Options_DefaultToAuto()
        => Assert.Equal(ProbabilityMode.Auto, new ChatDecisionClientOptions().ProbabilityMode);

    [Fact]
    public void GetService_ReturnsMetadata_FromTheChatClient()
    {
        var endpoint = new Uri("https://openrouter.ai/api/v1");
        var chat = Unanswered(new ChatClientMetadata("openrouter", endpoint, "m1"));

        using var client = chat.AsDecisionClient();
        using var overridden = chat.AsDecisionClient(new ChatDecisionClientOptions { Model = "m2" });

        Assert.Equal(new DecisionClientMetadata("openrouter", endpoint, "m1"), client.GetService<DecisionClientMetadata>());
        Assert.Equal(new DecisionClientMetadata("openrouter", endpoint, "m2"), overridden.GetService<DecisionClientMetadata>());
    }

    [Fact]
    public void GetService_ReturnsMetadata_WhenTheChatClientHasNone()
    {
        using var client = Unanswered().AsDecisionClient();

        Assert.Equal(new DecisionClientMetadata("unknown", null, null), client.GetService<DecisionClientMetadata>());
    }

    [Fact]
    public void GetService_ReturnsTheClientItself()
    {
        using var client = Unanswered().AsDecisionClient();

        Assert.Same(client, client.GetService<ChatDecisionClient>());
        Assert.Same(client, client.GetService<IDecisionClient>());
    }

    [Fact]
    public void GetService_ForwardsUnknownTypes_ToTheChatClient()
    {
        var metadata = new ChatClientMetadata("openrouter");
        var chat = Unanswered(metadata);
        using var client = chat.AsDecisionClient();

        Assert.Same(chat, client.GetService(typeof(ScriptedChatClient)));
        Assert.Same(chat, client.GetService(typeof(IChatClient)));
        Assert.Same(metadata, client.GetService(typeof(ChatClientMetadata)));
        Assert.Null(client.GetService(typeof(Uri)));
    }

    [Fact]
    public async Task Dispose_LeavesTheChatClientUsable()
    {
        var chat = Unanswered();
        var client = chat.AsDecisionClient();

        client.Dispose();
        client.Dispose();

        Assert.False(chat.IsDisposed);
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(), State)));
    }

    [Fact]
    public void Constructor_RejectsANullChatClient()
    {
        Assert.Throws<ArgumentNullException>(() => new ChatDecisionClient(null!));
        Assert.Throws<ArgumentNullException>(() => ((IChatClient)null!).AsDecisionClient());
    }

    [Fact]
    public async Task EvaluateAsync_RejectsANullRequest()
    {
        using var client = Unanswered().AsDecisionClient();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.EvaluateAsync(null!));
    }

    // A chat client that fails the test if the bridge ever calls it.
    private static ScriptedChatClient Unanswered(ChatClientMetadata? metadata = null)
        => new((_, _) => throw new InvalidOperationException("The chat client was called."), metadata);
}
