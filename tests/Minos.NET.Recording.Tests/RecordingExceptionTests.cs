namespace Minos.Recording.Tests;

public sealed class RecordingExceptionTests
{
    [Fact]
    public void TheStandardConstructors_KeepTheirArguments()
    {
        var inner = new InvalidOperationException("inner");

        Assert.Null(new RecordingException().InnerException);
        Assert.Equal("custom", new RecordingException("custom").Message);
        var both = new RecordingException("custom", inner);
        Assert.Equal("custom", both.Message);
        Assert.Same(inner, both.InnerException);
    }

    [Fact]
    public void TheRecordersOwnExceptions_CarryTheFixedMessages()
    {
        var inner = new InvalidOperationException("SENTINEL in the sink's message");

        var sinkFailed = RecordingException.SinkFailed(inner);
        var lost = RecordingException.Lost();

        Assert.Equal(RecordingException.SinkFailedMessage, sinkFailed.Message);
        Assert.Same(inner, sinkFailed.InnerException);
        Assert.Equal(RecordingException.LostMessage, lost.Message);
        Assert.IsType<TimeoutException>(lost.InnerException);
    }
}
