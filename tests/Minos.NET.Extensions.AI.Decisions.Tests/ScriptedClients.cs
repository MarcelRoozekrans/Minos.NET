using Microsoft.Extensions.AI;
using ZeroAlloc.Results;
using MinosClient = Minos.IDecisionClient;
using MinosMetadata = Minos.DecisionClientMetadata;
using MinosRequest = Minos.DecisionRequest;
using MinosResponse = Minos.DecisionResponse;
using UpstreamClient = Microsoft.Extensions.AI.IDecisionClient;
using UpstreamMetadata = Microsoft.Extensions.AI.DecisionClientMetadata;
using UpstreamRequest = Microsoft.Extensions.AI.DecisionRequest;
using UpstreamResponse = Microsoft.Extensions.AI.DecisionResponse;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// One scripted answer, written once and given by either side's fake: an upstream fake returns it as an upstream answer
/// with no <c>minos.</c> keys, as a real upstream provider would, and a Minos fake returns the Minos answer that
/// <see cref="UpstreamDecisionClient"/> reads from it, with the assumptions of docs/extensions-ai-decisions/mapping.md:
/// the confidence is the probability of the chosen option, or of the most probable level for a Score, the Score's level
/// is the most probable one, the lower on a tie, and the probability source is <see cref="ProbabilitySource.Model"/>.
/// </summary>
internal sealed record ScriptedAnswer(QuestionKind Kind, double Probability, int Chosen, double[] Probabilities, double Expected)
{
    public static ScriptedAnswer Noul(double probability) => new(QuestionKind.Noul, probability, 0, [], 0);

    public static ScriptedAnswer Choice(int chosen, params double[] probabilities) => new(QuestionKind.Choice, 0, chosen, probabilities, 0);

    public static ScriptedAnswer Score(double expected, params double[] probabilities)
        => new(QuestionKind.Score, 0, MostProbable(probabilities), probabilities, expected);

    /// <summary>A plausible answer for any question of <paramref name="kind"/> with <paramref name="count"/> options or levels.</summary>
    public static ScriptedAnswer For(QuestionKind kind, int count)
    {
        if (kind == QuestionKind.Noul)
        {
            return Noul(0.8);
        }

        // Rising weights 1, 2, ... n over their sum: a distribution that sums to 1 with a clear most probable entry.
        var total = count * (count + 1) / 2.0;
        var probabilities = new double[count];
        var expected = 0.0;
        for (var i = 0; i < count; i++)
        {
            probabilities[i] = (i + 1) / total;
            expected += i * probabilities[i];
        }

        return kind == QuestionKind.Choice ? Choice(count - 1, probabilities) : Score(expected, probabilities);
    }

    public QuestionAnswer ToMinos() => Kind switch
    {
        QuestionKind.Noul => QuestionAnswer.Noul(Probability),
        QuestionKind.Choice => QuestionAnswer.Choice(Chosen, Probabilities[Chosen], [.. Probabilities]),
        _ => QuestionAnswer.Score(Chosen, Expected, Probabilities[Chosen], [.. Probabilities]),
    };

    public DecisionAnswer ToUpstream(DecisionQuestion question) => question switch
    {
        BinaryDecisionQuestion => new BinaryDecisionAnswer(question.Id, Probability, DecisionPrecision.TwoDecimalPlaces),
        ChoiceDecisionQuestion choice => new ChoiceDecisionAnswer(
            question.Id,
            choice.Candidates[Chosen].Id,
            [.. choice.Candidates.Select((candidate, i) => new DecisionProbability(candidate.Id, Probabilities[i]))],
            DecisionPrecision.TwoDecimalPlaces),
        ScoreDecisionQuestion score => new ScoreDecisionAnswer(
            question.Id,
            Expected,
            [.. score.Levels.Select((level, i) => new DecisionProbability(level.Id, Probabilities[i]))],
            DecisionPrecision.TwoDecimalPlaces),
        _ => throw new InvalidOperationException("Unknown question type."),
    };

    private static int MostProbable(double[] probabilities)
    {
        var best = 0;
        for (var i = 1; i < probabilities.Length; i++)
        {
            if (probabilities[i] > probabilities[best])
            {
                best = i;
            }
        }

        return best;
    }
}

/// <summary>
/// An upstream client that answers each question from its script by id, or with <see cref="ScriptedAnswer.For"/> when the
/// script has none, and records the question ids of each call. <see cref="Failure"/>, when set, is thrown instead.
/// </summary>
internal sealed class ScriptedUpstreamClient(string providerName, string? model = null, UsageDetails? usage = null) : UpstreamClient
{
    public Dictionary<string, ScriptedAnswer> Script { get; } = new(StringComparer.Ordinal);

    public Exception? Failure { get; set; }

    public List<string[]> Calls { get; } = [];

    public Task<UpstreamResponse> GetResponseAsync(UpstreamRequest request, DecisionOptions? options = null, CancellationToken cancellationToken = default)
    {
        Calls.Add([.. request.Questions.Select(q => q.Id)]);
        if (Failure is { } failure)
        {
            return Task.FromException<UpstreamResponse>(failure);
        }

        var answers = request.Questions.Select(q => AnswerFor(q).ToUpstream(q)).ToArray();
        return Task.FromResult(new UpstreamResponse(request, answers, new DecisionProvenance(providerName, modelId: model), usage));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(UpstreamMetadata))
        {
            return new UpstreamMetadata(providerName);
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose()
    {
    }

    private ScriptedAnswer AnswerFor(DecisionQuestion question)
    {
        if (Script.TryGetValue(question.Id, out var answer))
        {
            return answer;
        }

        return question switch
        {
            ChoiceDecisionQuestion choice => ScriptedAnswer.For(QuestionKind.Choice, choice.Candidates.Count),
            ScoreDecisionQuestion score => ScriptedAnswer.For(QuestionKind.Score, score.Levels.Count),
            _ => ScriptedAnswer.For(QuestionKind.Noul, 0),
        };
    }
}

/// <summary>
/// A Minos client that answers each question from its script by key, or with <see cref="ScriptedAnswer.For"/> when the
/// script has none, and records the question keys of each call. <see cref="Error"/>, when set, is returned instead;
/// <see cref="Respond"/>, when set, builds the whole response.
/// </summary>
internal sealed class ScriptedMinosClient(string providerName, string? model = null, DecisionUsage? usage = null) : MinosClient
{
    public Dictionary<string, ScriptedAnswer> Script { get; } = new(StringComparer.Ordinal);

    public DecisionError? Error { get; set; }

    public Func<MinosRequest, MinosResponse>? Respond { get; set; }

    public List<string[]> Calls { get; } = [];

    public ValueTask<Result<MinosResponse, DecisionError>> EvaluateAsync(MinosRequest request, CancellationToken cancellationToken = default)
    {
        var questions = request.Definition.Questions;
        Calls.Add([.. questions.Select(q => q.Key)]);
        if (Error is { } error)
        {
            return new(Result<MinosResponse, DecisionError>.Failure(error));
        }

        if (Respond is { } respond)
        {
            return new(Result<MinosResponse, DecisionError>.Success(respond(request)));
        }

        var answers = questions.Select(q => AnswerFor(q).ToMinos()).ToArray();
        return new(Result<MinosResponse, DecisionError>.Success(new MinosResponse(request.Definition, answers, model, usage)));
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType.IsInstanceOfType(this))
        {
            return this;
        }

        if (serviceType == typeof(MinosMetadata))
        {
            return new MinosMetadata(providerName, null, model);
        }

        return serviceType == typeof(DecisionCapabilities)
            ? new DecisionCapabilities { QuestionKinds = QuestionKinds.All, MaxQuestions = null, MaxChoiceOptions = null, SupportsImages = false, SupportsModelListing = false }
            : null;
    }

    public void Dispose()
    {
    }

    private ScriptedAnswer AnswerFor(QuestionDefinition question)
        => Script.TryGetValue(question.Key, out var answer) ? answer : ScriptedAnswer.For(question.Kind, question.Options.Count);
}

/// <summary>Compares Minos answers field by field: <see cref="QuestionAnswer"/> has no equality of its own.</summary>
internal static class AnswerAssert
{
    public static void Same(QuestionAnswerList expected, QuestionAnswerList actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Same(expected[i], actual[i]);
        }
    }

    public static void Same(QuestionAnswer expected, QuestionAnswer actual)
    {
        Assert.Equal(expected.Key, actual.Key);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Probability, actual.Probability);
        Assert.Equal(expected.Expected, actual.Expected);
        Assert.Equal(expected.ProbabilitySource, actual.ProbabilitySource);
        Assert.Equal(expected.Probabilities.ToArray(), actual.Probabilities.ToArray());
        if (expected.Kind != QuestionKind.Noul)
        {
            Assert.Equal(expected.ChosenIndex, actual.ChosenIndex);
            Assert.Equal(expected.Confidence, actual.Confidence);
        }
    }
}
