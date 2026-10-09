using Minos.Telemetry;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Tests;

/// <summary>
/// An <see cref="IJevOperations"/> that answers every call from one response body, so the generated proxy's spans and
/// metrics can be checked without a transport.
/// </summary>
internal sealed class FakeOperations(string responseJson) : IJevOperations
{
    private readonly string _responseJson = responseJson;

    /// <summary>Gets or sets the error every call fails with; <see langword="null"/> succeeds.</summary>
    public JevError? Error { get; set; }

    /// <summary>Gets or sets whether each call yields first, so it completes asynchronously.</summary>
    public bool Yield { get; set; }

    /// <summary>Gets or sets a delay each call waits first, so its duration has a known minimum; zero waits none.</summary>
    public TimeSpan Delay { get; set; }

    /// <summary>Gets or sets whether a typed result's response is returned already disposed, so reading it throws.</summary>
    public bool DisposedResponse { get; set; }

    /// <summary>Gets or sets the exception every call throws, after it yields; <see langword="null"/> throws none.</summary>
    public Exception? Throws { get; set; }

    /// <summary>Gets or sets the raw path's response.</summary>
    public SystemOneResponse? Raw { get; set; }

    /// <summary>Gets or sets the model list.</summary>
    public ModelList Models { get; set; } = new() { Models = [] };

    public ValueTask<Result<SystemOneResponse, JevError>> EvaluateAsync(
        SystemOneRequest request, string provider, Uri endpoint, CancellationToken ct)
        => Complete(Error is { } error
            ? Result<SystemOneResponse, JevError>.Failure(error)
            : Result<SystemOneResponse, JevError>.Success(Raw ?? throw new InvalidOperationException("Set Raw first.")));

    public ValueTask<Result<Evaluated<T>, JevError>> EvaluateTypedAsync<T>(
        RawJson body, string model, string provider, Uri endpoint, int questionCount, CancellationToken ct)
        where T : IJevQuestionSet<T>
    {
        body.Dispose();
        return Complete(Typed(GeneratedAnswerParser<T>.Instance));
    }

    public ValueTask<Result<Evaluated<JevAnswers>, JevError>> EvaluateBuiltSetAsync(
        RawJson body, JevQuestionSet questionSet, string model, string provider, Uri endpoint, CancellationToken ct)
    {
        body.Dispose();
        return Complete(Typed(questionSet.Parser));
    }

    public ValueTask<Result<ModelList, JevError>> ListModelsAsync(string provider, Uri endpoint, CancellationToken ct)
        => Complete(Error is { } error ? Result<ModelList, JevError>.Failure(error) : Result<ModelList, JevError>.Success(Models));

    private Result<Evaluated<TResult>, JevError> Typed<TResult>(AnswerParser<TResult> parse)
    {
        if (Error is { } error)
        {
            return Result<Evaluated<TResult>, JevError>.Failure(error);
        }

        var response = TelemetryBodies.RawJsonOf(_responseJson);
        var answers = TypedEvaluation.ParseResponse(response.Span, parse).Value;
        if (DisposedResponse)
        {
            response.Dispose();
        }

        return Result<Evaluated<TResult>, JevError>.Success(new Evaluated<TResult>(answers, response));
    }

    private ValueTask<TResult> Complete<TResult>(TResult result)
        => Yield || Throws is not null || Delay > TimeSpan.Zero ? LaterAsync(result) : new ValueTask<TResult>(result);

    private async ValueTask<TResult> LaterAsync<TResult>(TResult result)
    {
        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay);
        }
        else
        {
            await Task.Yield();
        }

        if (Throws is { } thrown)
        {
            throw thrown;
        }

        return result;
    }
}
