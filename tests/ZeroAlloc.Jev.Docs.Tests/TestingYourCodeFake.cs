namespace ZeroAlloc.Jev.Docs.Tests;

#region TestingYourCode_Fake
using System.Text.Json;
using ZeroAlloc.Jev;
using ZeroAlloc.Results;

// A fake implements the two abstract members. Every other member of IJevClient has a default that calls EvaluateAsync.
public sealed class FakeJev(Result<SystemOneResponse, JevError> reply) : IJevClient
{
    private readonly List<SystemOneRequest> _requests = [];

    // Every request the code under test sent, so a test can check what was asked.
    public IReadOnlyList<SystemOneRequest> Requests => _requests;

    // A reply that answers both questions of TriageQuestions.
    public static FakeJev Answering(double urgent, TriageDesk desk, double deskConfidence)
    {
        // A Choice names its options by the enum member in snake_case, so ProductTeam is product_team.
        var probabilities = new Dictionary<string, double>();
        foreach (var option in Enum.GetValues<TriageDesk>())
        {
            probabilities[JsonNamingPolicy.SnakeCaseLower.ConvertName(option.ToString())] = option == desk ? 0.7 : 0.15;
        }

        return new FakeJev(Result<SystemOneResponse, JevError>.Success(new SystemOneResponse
        {
            Model = "fake",
            Usage = new JevUsage { InputTokens = 1, OutputTokens = 1 },
            Answers = new Dictionary<string, JevAnswer>
            {
                // The keys are the wire keys of the questions.
                ["is_urgent"] = new NoulAnswer { Noul = urgent },
                ["desk"] = new ChoiceAnswer
                {
                    Choice = JsonNamingPolicy.SnakeCaseLower.ConvertName(desk.ToString()),
                    Confidence = deskConfidence,
                    Probabilities = probabilities,
                },
            },
        }));
    }

    // A reply that is a failure, as a rejected key or a network error would be.
    public static FakeJev Failing(JevErrorKind kind)
        => new(Result<SystemOneResponse, JevError>.Failure(new JevError(kind, "The fake failed on purpose.")));

    // A busy service: the kind and message are the constructor's, the rest are init properties.
    public static FakeJev Overloaded(TimeSpan retryAfter)
        => new(Result<SystemOneResponse, JevError>.Failure(new JevError(JevErrorKind.Overloaded, "The fake is busy on purpose.")
        {
            StatusCode = 503,
            RetryAfter = retryAfter,
        }));

    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        return ValueTask.FromResult(reply);
    }

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
        => throw new NotSupportedException("This fake does not list models.");
}
#endregion
