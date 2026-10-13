using System.Globalization;
using System.Text.Json;
using Minos.Telemetry;

namespace Minos;

/// <summary>Why a call failed.</summary>
/// <remarks>
/// Every failure a call can have is returned as a <see cref="DecisionError"/>. Only programming errors, such as a missing
/// API key or a <see langword="null"/> request, and cancellation the caller asked for are thrown.
/// </remarks>
public sealed class DecisionError
{
    /// <summary>Initializes a new instance of the <see cref="DecisionError"/> class.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="message">A short, human-readable description.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> is <see langword="null"/>.</exception>
    public DecisionError(DecisionErrorKind kind, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Kind = kind;
        Message = message;
    }

    /// <summary>Initializes a new error of <paramref name="kind"/> that lists the questions it is about, such as a <see cref="DecisionErrorKind.Refused"/> error naming each refused question.</summary>
    /// <param name="kind">What went wrong.</param>
    /// <param name="message">A short, human-readable description.</param>
    /// <param name="failures">The questions and the rules they break.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="failures"/> is <see langword="null"/>.</exception>
    public DecisionError(DecisionErrorKind kind, string message, IReadOnlyList<QuestionFailure> failures)
        : this(kind, message)
    {
        ArgumentNullException.ThrowIfNull(failures);

        Failures = failures.Count == 0 ? [] : new System.Collections.ObjectModel.ReadOnlyCollection<QuestionFailure>([.. failures]);
    }

    /// <summary>Initializes a new <see cref="DecisionErrorKind.InvalidQuestions"/> error.</summary>
    /// <param name="message">A short, human-readable description.</param>
    /// <param name="failures">The rules the question set breaks.</param>
    internal DecisionError(string message, IReadOnlyList<QuestionFailure> failures)
        : this(DecisionErrorKind.InvalidQuestions, message, failures)
    {
    }

    /// <summary>Gets what went wrong.</summary>
    public DecisionErrorKind Kind { get; }

    /// <summary>Gets a short, human-readable description.</summary>
    public string Message { get; }

    /// <summary>Gets the HTTP status code, or <see langword="null"/> when no response arrived.</summary>
    public int? StatusCode { get; init; }

    /// <summary>
    /// Gets how long the service asked the caller to wait, from the <c>retry-after-ms</c> header, which takes
    /// precedence, or the <c>Retry-After</c> header, or <see langword="null"/> when neither was set.
    /// </summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Gets the error response body when it is JSON, for example the field a 422 rejected.</summary>
    public JsonElement? Detail { get; init; }

    /// <summary>
    /// Gets the provider's id for the failed request, from the error response's <c>x-request-id</c>,
    /// <c>x-typesafe-request-id</c> or <c>request-id</c> header, the first present in that order. It is
    /// <see langword="null"/> when the response carried none, and for every failure without an HTTP error response, such
    /// as a network failure, a time-out or a refusal. Quote it when you report a failed call to the provider.
    /// </summary>
    public string? RequestId { get; init; }

    /// <summary>Gets the exception behind a network, time-out or response-reading failure.</summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the questions a failure is about, with the rules they break: those a question set built with
    /// <see cref="QuestionSetBuilder"/> breaks when <see cref="Kind"/> is <see cref="DecisionErrorKind.InvalidQuestions"/>, and
    /// those a provider cannot answer when <see cref="Kind"/> is <see cref="DecisionErrorKind.Unsupported"/>; empty for every
    /// other failure.
    /// </summary>
    public IReadOnlyList<QuestionFailure> Failures { get; } = [];

    /// <summary>Gets <see cref="Kind"/>'s name, the span's and the duration metric's <c>error.type</c>.</summary>
    internal string ErrorType => DecisionTelemetry.ErrorTypeOf(Kind);

    /// <inheritdoc />
    public override string ToString()
        => (StatusCode, RequestId) switch
        {
            ({ } status, { } requestId) => $"{Kind.ToString()} ({status.ToString(CultureInfo.InvariantCulture)}, request {requestId}): {Message}",
            ({ } status, null) => $"{Kind.ToString()} ({status.ToString(CultureInfo.InvariantCulture)}): {Message}",
            (null, { } requestId) => $"{Kind.ToString()} (request {requestId}): {Message}",
            _ => $"{Kind.ToString()}: {Message}",
        };
}
