namespace Minos.Tests;

public sealed class DecisionErrorTests
{
    [Fact]
    public void Failures_constructor_keeps_the_kind()
    {
        var failure = new QuestionFailure("MIN201", "topic", "Custom does not support Choice questions.");
        var error = new DecisionError(DecisionErrorKind.Unsupported, failure.Message, [failure]);

        Assert.Equal(DecisionErrorKind.Unsupported, error.Kind);
        Assert.Equal(failure.Message, error.Message);
        Assert.Equal([failure], error.Failures);
    }

    [Fact]
    public void Failures_constructor_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>("failures", () => new DecisionError(DecisionErrorKind.Refused, "Refused.", null!));
        Assert.Throws<ArgumentNullException>("message", () => new DecisionError(DecisionErrorKind.Refused, null!, []));
    }

    [Fact]
    public void Failures_constructor_copies_the_list()
    {
        var failures = new List<QuestionFailure> { new("refusal", "is_urgent", "The model refused.") };
        var error = new DecisionError(DecisionErrorKind.Refused, "Refused.", failures);

        failures.Clear();

        Assert.Single(error.Failures);
    }

    [Fact]
    public void Disposed_error_carries_the_exception()
    {
        var exception = new ObjectDisposedException("client");
        var error = DecisionErrors.Disposed(exception);

        Assert.Equal(DecisionErrorKind.Disposed, error.Kind);
        Assert.Equal(DecisionErrors.DisposedMessage, error.Message);
        Assert.Same(exception, error.Exception);
    }
}
