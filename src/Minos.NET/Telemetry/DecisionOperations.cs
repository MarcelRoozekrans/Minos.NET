using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Telemetry;

/// <summary>
/// The client's four operations over its retry proxy. It holds no telemetry code: <see cref="DecisionClient"/> calls it
/// through the generated <c>DecisionOperationsInstrumented</c>. The provider, endpoint, model and question count are for the
/// proxy's tags and unused here.
/// </summary>
/// <param name="api">The retry proxy over the transport.</param>
/// <param name="authorization">The <c>Authorization</c> header value.</param>
internal sealed class DecisionOperations(IDecisionApi api, string authorization) : IDecisionOperations
{
    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs, since
    // SystemOneResponse is non-nullable in IDecisionApi; only a null value nested inside a non-null response, such as an
    // answer, still needs to be caught here.
    public async ValueTask<Result<SystemOneResponse, DecisionError>> EvaluateAsync(
        SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
    {
        var result = await api.EvaluateAsync(request, authorization, retryCount: null, ct).ConfigureAwait(false);

        if (result.IsSuccess && HasNullAnswer(result.Value))
        {
            return Result<SystemOneResponse, DecisionError>.Failure(Unreadable("The response contains a null answer."));
        }

        return result;
    }

    public ValueTask<Result<Evaluated<T>, DecisionError>> EvaluateTypedAsync<T>(
        RawJson body, string model, string provider, Uri endpoint, int questionCount, CancellationToken ct)
        where T : IQuestionSet<T>
        => EvaluateRawAsync(body, GeneratedAnswerParser<T>.Instance, ct);

    public ValueTask<Result<Evaluated<Answers>, DecisionError>> EvaluateBuiltSetAsync(
        RawJson body, QuestionSet questionSet, string model, string provider, Uri endpoint, CancellationToken ct)
        => EvaluateRawAsync(body, questionSet.Parser, ct);

    // ZeroAlloc.Rest 3.0 itself rejects an empty or null success body as a Deserialization error before this runs,
    // since ModelList is non-nullable in IDecisionApi, so no null check remains needed here.
    public ValueTask<Result<ModelList, DecisionError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
        => api.ListModelsAsync(authorization, retryCount: null, ct);

    // Owns body: the retry proxy sends the same instance on every attempt, so it is disposed only once the whole call,
    // retries included, has completed. A successful response is the only RawJson the transport hands back; failed
    // attempts carry a DecisionError and no buffer, so a failed transport result needs no dispose. A successful parse hands
    // the response to the Evaluated, which the caller disposes after the proxy has read it; every other path returns
    // it here.
    private async ValueTask<Result<Evaluated<TResult>, DecisionError>> EvaluateRawAsync<TResult>(
        RawJson body, AnswerParser<TResult> parse, CancellationToken ct)
    {
        RawJson? response = null;
        try
        {
            var result = await api.EvaluateRawAsync(body, authorization, retryCount: null, ct).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return Result<Evaluated<TResult>, DecisionError>.Failure(result.Error);
            }

            response = result.Value;
            var parsed = TypedEvaluation.ParseResponse(response.Span, parse, statusCode: 200);
            if (parsed.IsFailure)
            {
                return Result<Evaluated<TResult>, DecisionError>.Failure(parsed.Error);
            }

            var evaluated = new Evaluated<TResult>(parsed.Value, response);
            response = null;
            return Result<Evaluated<TResult>, DecisionError>.Success(evaluated);
        }
        finally
        {
            response?.Dispose();
            body.Dispose();
        }
    }

    // System.Text.Json does not apply nullable annotations to dictionary values, so a null answer can arrive.
    private static bool HasNullAnswer(SystemOneResponse response)
    {
        foreach (var answer in response.Answers.Values)
        {
            if (answer is null)
            {
                return true;
            }
        }

        return false;
    }

    // statusCode 200 is a placeholder: ZeroAlloc.Rest's generated client does not expose the real status of a
    // successful response that this client itself then rejects as unreadable, so 200 is kept only because that is
    // the status that let the response through in the first place.
    private static DecisionError Unreadable(string message) => new(DecisionErrorKind.InvalidResponse, message) { StatusCode = 200 };
}
