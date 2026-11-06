using Minos.Samples.TicketTriage;
using ZeroAlloc.Results;

namespace Minos.Samples.Tests;

public sealed class TriageStagesTests
{
    private static readonly QuestionSetDefinition Urgency =
        new(QuestionDefinition.Noul("is_urgent", DecisionContent.FromString("Is this urgent?")));

    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    // A clock the scripted clients move on by hand; 1000 ticks per second.
    private sealed class ManualTime : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => 1000;

        public override long GetTimestamp() => _ticks;

        public void AdvanceMs(long ms) => _ticks += ms;
    }

    private sealed class Scripted(Func<DecisionRequest, Result<DecisionResponse, DecisionError>> answer) : IDecisionClient
    {
        public int Calls { get; private set; }

        public ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return new(answer(request));
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private static Scripted Answering(double probability, ManualTime? time = null, long takesMs = 0)
        => new(request =>
        {
            time?.AdvanceMs(takesMs);
            // Answers the definition it is asked, as escalation needs when it asks a subset of the questions.
            return Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, [QuestionAnswer.Noul(probability)]));
        });

    private static Scripted Failing(ManualTime? time = null, long takesMs = 0)
        => new(_ =>
        {
            time?.AdvanceMs(takesMs);
            return Result<DecisionResponse, DecisionError>.Failure(new DecisionError(DecisionErrorKind.Server, "failed"));
        });

    private static async Task<Result<DecisionResponse, DecisionError>> Run(IDecisionClient local, IDecisionClient target, CallSlot slot)
    {
        using var client = new LocalCaptureClient(local, slot).AsBuilder().UseEscalation(target, o => o.TargetName = "hosted").Build();
        return await client.EvaluateAsync(new DecisionRequest(Urgency, State));
    }

    [Fact]
    public async Task Capture_KeepsTheLocalResponse_WhenTheQuestionIsEscalated()
    {
        var slot = new CallSlot();
        var target = Answering(0.99);

        var result = await Run(Answering(0.5), target, slot);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, target.Calls);
        Assert.NotNull(slot.Response);
        Assert.Equal(0.5, slot.Response.Answers[0].Probability, 6);
        Assert.Equal(0.99, result.Value.Answers[0].Probability, 6);
    }

    [Fact]
    public async Task Capture_KeepsTheLocalResponse_WhenItIsNotEscalated()
    {
        var slot = new CallSlot();
        var target = Answering(0.99);

        var result = await Run(Answering(0.02), target, slot);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, target.Calls);
        Assert.NotNull(slot.Response);
        Assert.Equal(0.02, slot.Response.Answers[0].Probability, 6);
    }

    [Fact]
    public async Task Capture_RecordsLatencyFromTheTimeProvider()
    {
        var time = new ManualTime();
        var slot = new CallSlot();
        using var client = new LocalCaptureClient(Answering(0.5, time, 120), slot, time);

        await client.EvaluateAsync(new DecisionRequest(Urgency, State));

        Assert.Equal(120, slot.LatencyMs);
    }

    [Fact]
    public async Task Timing_RecordsLatencyOfAFailingTarget()
    {
        var time = new ManualTime();
        var slot = new CallSlot();
        using var client = new TimingDecisionClient(Failing(time, 80), slot, time);

        var result = await client.EvaluateAsync(new DecisionRequest(Urgency, State));

        Assert.True(result.IsFailure);
        Assert.Equal(80, slot.LatencyMs);
        Assert.Null(slot.Response);
    }

    [Fact]
    public async Task Capture_StoresNothing_WhenTheLocalCallFails()
    {
        var slot = new CallSlot();
        using var client = new LocalCaptureClient(Failing(), slot);

        var result = await client.EvaluateAsync(new DecisionRequest(Urgency, State));

        Assert.True(result.IsFailure);
        Assert.Null(slot.Response);
        Assert.Null(slot.LatencyMs);
    }

    [Fact]
    public void Reset_ClearsTheSlot()
    {
        var slot = new CallSlot { Response = new DecisionResponse(Urgency, [QuestionAnswer.Noul(0.5)]), LatencyMs = 3 };

        slot.Reset();

        Assert.Null(slot.Response);
        Assert.Null(slot.LatencyMs);
    }
}
