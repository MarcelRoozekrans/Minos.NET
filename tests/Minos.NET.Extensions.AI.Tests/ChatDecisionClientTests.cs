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
    public async Task Images_ReachTheChatClientAsDataParts_WhenSupported()
    {
        var chat = new ScriptedChatClient((_, _) => ChatCompletions.WithTopLogprobs(("A", -0.1f), ("B", -2.3f)));
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions
        {
            ProbabilityMode = ProbabilityMode.Logprobs,
            Capabilities = new DecisionCapabilities { SupportsImages = true },
        });
        var image = DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
        var request = new DecisionRequest(new QuestionSetDefinition(QuestionDefinition.Noul("refund", "Does the customer ask for a refund?")), State)
        {
            Images = [image],
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var user = Assert.Single(Assert.Single(chat.Calls).Messages, message => message.Role == ChatRole.User);
        var data = Assert.Single(user.Contents.OfType<DataContent>());
        Assert.Equal("image/png", data.MediaType);
        Assert.Equal(image.Data.ToArray(), data.Data.ToArray());
    }

    [Fact]
    public async Task CapabilityFailures_KeepTheCoresOrder_WithAScoreOfTooManyLevels()
    {
        var chat = Unanswered();
        using var client = chat.AsDecisionClient(new ChatDecisionClientOptions
        {
            Capabilities = new DecisionCapabilities { MaxQuestions = 2, QuestionKinds = QuestionKinds.Noul | QuestionKinds.Score, MaxChoiceOptions = 2 },
        });
        var levels = Enumerable.Range(0, 27).Select(i => Criterion.Text("Level " + i)).ToArray();
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Score("severity", "How severe is it?", levels),
            QuestionDefinition.Choice(
                "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null)),
            QuestionDefinition.Noul("refund", "Does the customer ask for a refund?"));
        var request = new DecisionRequest(definition, State)
        {
            Images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")],
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal(DecisionErrorKind.Unsupported, result.Error.Kind);
        Assert.Equal(
            new (string, string?)[] { ("MIN202", null), ("MIN203", "severity"), ("MIN201", "team"), ("MIN203", "team"), ("MIN206", null) },
            result.Error.Failures.Select(failure => (failure.Rule, failure.QuestionKey)));
        Assert.Equal(result.Error.Failures[0].Message, result.Error.Message);
        Assert.Empty(chat.Calls);
    }

    [Fact]
    public async Task AScoreOfTooManyLevels_ComesBeforeTheImageFailure()
    {
        var chat = Unanswered();
        using var client = chat.AsDecisionClient();
        var levels = Enumerable.Range(0, 27).Select(i => Criterion.Text("Level " + i)).ToArray();
        var request = new DecisionRequest(new QuestionSetDefinition(QuestionDefinition.Score("severity", "How severe is it?", levels)), State)
        {
            Images = [DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png")],
        };

        var result = await client.EvaluateAsync(request);

        Assert.True(result.IsFailure);
        Assert.Equal(["MIN203", "MIN206"], result.Error.Failures.Select(failure => failure.Rule));
        Assert.Contains("severity", result.Error.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Options_WithADefinedProbabilityMode_AreAccepted(int mode)
    {
        using var client = Unanswered().AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = (ProbabilityMode)mode });

        Assert.NotNull(client);
    }

    [Theory]
    [InlineData(7)]
    [InlineData(-1)]
    public void Options_WithAnUndefinedProbabilityMode_AreRejected(int mode)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Unanswered().AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = (ProbabilityMode)mode }));

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("ProbabilityMode", exception.Message, StringComparison.Ordinal);
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

        Assert.Equal(new DecisionClientMetadata("openrouter", endpoint, "m1") { CacheScope = "auto" }, client.GetService<DecisionClientMetadata>());
        Assert.Equal(new DecisionClientMetadata("openrouter", endpoint, "m2") { CacheScope = "auto" }, overridden.GetService<DecisionClientMetadata>());
    }

    [Fact]
    public void GetService_ReturnsMetadata_WhenTheChatClientHasNone()
    {
        using var client = Unanswered().AsDecisionClient();

        Assert.Equal(new DecisionClientMetadata("unknown", null, null) { CacheScope = "auto/" + typeof(ScriptedChatClient).FullName }, client.GetService<DecisionClientMetadata>());
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
