namespace Minos.Tests;

public sealed class FallbackOptionsTests
{
    private const string ParamName = "configure";

    private static StubDecisionClient Client() => StubDecisionClient.Failing(DecisionErrorKind.Server);

    private static FallbackOptions Options(params string[] stepNames)
    {
        var options = new FallbackOptions();
        foreach (var name in stepNames)
        {
            options.Steps.Add(new FallbackStep(name, Client()));
        }

        return options;
    }

    [Theory]
    [InlineData(DecisionErrorKind.Unauthorized)]
    [InlineData(DecisionErrorKind.RateLimited)]
    [InlineData(DecisionErrorKind.Overloaded)]
    [InlineData(DecisionErrorKind.Server)]
    [InlineData(DecisionErrorKind.Http)]
    [InlineData(DecisionErrorKind.Network)]
    [InlineData(DecisionErrorKind.Timeout)]
    [InlineData(DecisionErrorKind.InvalidResponse)]
    [InlineData(DecisionErrorKind.Unsupported)]
    public void IsProviderFailure_moves_on_for_provider_kinds(DecisionErrorKind kind)
        => Assert.True(FallbackOptions.IsProviderFailure(new DecisionError(kind, "failed")));

    [Theory]
    [InlineData(DecisionErrorKind.Validation)]
    [InlineData(DecisionErrorKind.InvalidQuestions)]
    [InlineData(DecisionErrorKind.Refused)]
    [InlineData(DecisionErrorKind.Disposed)]
    public void IsProviderFailure_returns_request_and_refusal_kinds(DecisionErrorKind kind)
        => Assert.False(FallbackOptions.IsProviderFailure(new DecisionError(kind, "failed")));

    [Fact]
    public void IsProviderFailure_rejects_a_null_error()
        => Assert.Throws<ArgumentNullException>(() => FallbackOptions.IsProviderFailure(null!));

    [Fact]
    public void Defaults_are_primary_no_steps_no_budget_and_provider_failures()
    {
        var options = new FallbackOptions();

        Assert.Equal("primary", options.PrimaryName);
        Assert.Empty(options.Steps);
        Assert.Null(options.Budget);
        Assert.Equal<Func<DecisionError, bool>>(FallbackOptions.IsProviderFailure, options.ShouldFallBack);
        Assert.True(options.ShouldFallBack(new DecisionError(DecisionErrorKind.Server, "failed")));
        Assert.False(options.ShouldFallBack(new DecisionError(DecisionErrorKind.Refused, "failed")));
    }

    [Fact]
    public void Step_keeps_its_name_client_and_latency()
    {
        var client = Client();
        var step = new FallbackStep("clef", client) { ExpectedLatency = TimeSpan.FromMilliseconds(300) };

        Assert.Equal("clef", step.Name);
        Assert.Same(client, step.Client);
        Assert.Equal(TimeSpan.FromMilliseconds(300), step.ExpectedLatency);
        Assert.Equal(TimeSpan.Zero, new FallbackStep("clef", client).ExpectedLatency);
    }

    [Fact]
    public void Step_rejects_a_null_or_empty_name_or_a_null_client()
    {
        Assert.Throws<ArgumentNullException>(() => new FallbackStep(null!, Client()));
        Assert.Throws<ArgumentNullException>(() => new FallbackStep("clef", null!));
        Assert.Throws<ArgumentException>(() => new FallbackStep(string.Empty, Client()));
    }

    [Fact]
    public void Validate_accepts_a_valid_chain()
    {
        var options = Options("clef");
        options.Steps.Add(new FallbackStep("openai", Client()) { ExpectedLatency = TimeSpan.FromSeconds(1) });
        options.Budget = TimeSpan.FromSeconds(5);

        options.Validate(ParamName);
    }

    [Fact]
    public void Validate_rejects_no_steps()
    {
        var exception = Assert.Throws<ArgumentException>(() => new FallbackOptions().Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.Contains("FallbackOptions.Steps", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_a_null_step()
    {
        var options = Options("clef");
        options.Steps.Add(null!);

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
    }

    [Fact]
    public void Validate_rejects_an_empty_primary_name()
    {
        var options = Options("clef");
        options.PrimaryName = string.Empty;

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.Contains("FallbackOptions.PrimaryName", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_duplicate_step_names()
    {
        var options = Options("secret-step", "other", "secret-step");

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.DoesNotContain("secret-step", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_compares_step_names_ordinally()
    {
        var options = Options("clef", "Clef");
        options.PrimaryName = "PRIMARY";
        options.Steps.Add(new FallbackStep("primary", Client()));

        options.Validate(ParamName);
    }

    [Fact]
    public void Validate_rejects_a_step_named_like_the_primary()
    {
        var options = Options("clef", "secret-primary");
        options.PrimaryName = "secret-primary";

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.DoesNotContain("secret-primary", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_a_negative_expected_latency()
    {
        var options = Options("clef");
        options.Steps.Add(new FallbackStep("secret-step", Client()) { ExpectedLatency = TimeSpan.FromTicks(-1) });

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.Contains("ExpectedLatency", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-step", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Validate_rejects_a_non_positive_budget(long ticks)
    {
        var options = Options("secret-step");
        options.Budget = TimeSpan.FromTicks(ticks);

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.Contains("FallbackOptions.Budget", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-step", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_rejects_a_null_predicate()
    {
        var options = Options("secret-step");
        options.ShouldFallBack = null!;

        var exception = Assert.Throws<ArgumentException>(() => options.Validate(ParamName));

        Assert.Equal(ParamName, exception.ParamName);
        Assert.Contains("FallbackOptions.ShouldFallBack", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-step", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Clone_copies_steps_into_a_new_list()
    {
        Func<DecisionError, bool> predicate = _ => true;
        var options = Options("clef", "openai");
        options.PrimaryName = "local";
        options.Budget = TimeSpan.FromSeconds(5);
        options.ShouldFallBack = predicate;

        var clone = options.Clone();
        options.Steps.Clear();
        options.PrimaryName = "changed";
        options.Budget = null;

        Assert.NotSame(options.Steps, clone.Steps);
        Assert.Equal(["clef", "openai"], clone.Steps.Select(step => step.Name));
        Assert.Equal("local", clone.PrimaryName);
        Assert.Equal(TimeSpan.FromSeconds(5), clone.Budget);
        Assert.Same(predicate, clone.ShouldFallBack);
    }
}
