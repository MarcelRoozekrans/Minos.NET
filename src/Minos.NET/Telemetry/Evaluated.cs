using System.Runtime.InteropServices;
using Minos.Transport;
using ZeroAlloc.Results;

namespace Minos.Telemetry;

/// <summary>
/// A typed or built-set evaluation's answers, with the still-pooled response they were read from, so the instrumented
/// proxy can read the response's model, usage and confidences, only while it is listening.
/// </summary>
/// <remarks>
/// Copied by value through the proxy; every copy shares the one <see cref="RawJson"/>, which <see cref="Dispose"/> returns.
/// Each deferred member re-reads the response bytes on every access, so the cost is paid only by a listening call. The
/// model string is the one allocation, built anew on each read of <see cref="ResponseModel"/>; nothing is cached.
/// </remarks>
/// <typeparam name="T">The typed answers.</typeparam>
[StructLayout(LayoutKind.Auto)]
internal readonly struct Evaluated<T> : IDisposable
{
    private readonly RawJson _response;

    public Evaluated(T answers, RawJson response)
    {
        Answers = answers;
        _response = response;
    }

    /// <summary>Gets the parsed answers.</summary>
    public T Answers { get; }

    /// <summary>Gets the model that answered, from the top-level <c>model</c>, as a new string on every read.</summary>
    public string? ResponseModel => ResponseFields.Model(_response.Span);

    /// <summary>Gets <c>usage.input_tokens</c>.</summary>
    public int? InputTokens => ResponseFields.UsageInt32(_response.Span, "input_tokens"u8);

    /// <summary>Gets <c>usage.output_tokens</c>.</summary>
    public int? OutputTokens => ResponseFields.UsageInt32(_response.Span, "output_tokens"u8);

    /// <summary>Gets each Choice and Score answer's confidence.</summary>
    public ConfidenceValues Confidences => new(_response.Memory);

    /// <summary>Returns the response buffer; later calls do nothing.</summary>
    public void Dispose() => _response.Dispose();
}

/// <summary>Turns an instrumented typed call's result into the answers the caller gets.</summary>
internal static class Evaluated
{
    /// <summary>
    /// Returns the answers and the response buffer. A call that completed synchronously is unwrapped inline and
    /// allocates nothing; otherwise one async method awaits it, the only allocation telemetry adds when nothing listens.
    /// </summary>
    public static ValueTask<Result<T, DecisionError>> Unwrap<T>(ValueTask<Result<Evaluated<T>, DecisionError>> call)
        => call.IsCompletedSuccessfully ? new ValueTask<Result<T, DecisionError>>(Answers(call.Result)) : UnwrapAsync(call);

    private static async ValueTask<Result<T, DecisionError>> UnwrapAsync<T>(ValueTask<Result<Evaluated<T>, DecisionError>> call)
        => Answers(await call.ConfigureAwait(false));

    private static Result<T, DecisionError> Answers<T>(Result<Evaluated<T>, DecisionError> result)
    {
        if (result.IsFailure)
        {
            return Result<T, DecisionError>.Failure(result.Error);
        }

        var evaluated = result.Value;
        evaluated.Dispose();
        return Result<T, DecisionError>.Success(evaluated.Answers);
    }
}
