namespace ZeroAlloc.Jev.Docs.Tests;

#region TestingYourCode_Fake
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
        var other = desk == TriageDesk.Billing ? TriageDesk.Technical : TriageDesk.Billing;
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
                    Choice = desk.ToString().ToLowerInvariant(),
                    Confidence = deskConfidence,
                    Probabilities = new Dictionary<string, double>
                    {
                        [desk.ToString().ToLowerInvariant()] = 0.7,
                        [other.ToString().ToLowerInvariant()] = 0.3,
                    },
                },
            },
        }));
    }

    // A reply that is a failure, as a rejected key or a network error would be.
    public static FakeJev Failing(JevErrorKind kind)
        => new(Result<SystemOneResponse, JevError>.Failure(new JevError(kind, "The fake failed on purpose.")));

    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(SystemOneRequest request, CancellationToken ct)
    {
        _requests.Add(request);
        return ValueTask.FromResult(reply);
    }

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(CancellationToken ct = default)
        => throw new NotSupportedException("This fake does not list models.");
}
#endregion
