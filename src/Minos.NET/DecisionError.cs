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
    public DecisionError(DecisionErrorKind kind, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        Kind = kind;
        Message = message;
    }

    /// <summary>Initializes a new <see cref="DecisionErrorKind.InvalidQuestions"/> error.</summary>
    /// <param name="message">A short, human-readable description.</param>
    /// <param name="failures">The rules the question set breaks.</param>
    internal DecisionError(string message, IReadOnlyList<QuestionFailure> failures)
        : this(DecisionErrorKind.InvalidQuestions, message)
    {
        Failures = failures.Count == 0 ? [] : new System.Collections.ObjectModel.ReadOnlyCollection<QuestionFailure>([.. failures]);
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

    /// <summary>Gets the exception behind a network, time-out or response-reading failure.</summary>
    public Exception? Exception { get; init; }

    /// <summary>
    /// Gets the rules a question set built with <see cref="QuestionSetBuilder"/> breaks, when <see cref="Kind"/> is
    /// <see cref="DecisionErrorKind.InvalidQuestions"/>; empty for every other kind.
    /// </summary>
    public IReadOnlyList<QuestionFailure> Failures { get; } = [];

    /// <summary>Gets <see cref="Kind"/>'s name, the span's and the duration metric's <c>error.type</c>.</summary>
    internal string ErrorType => DecisionTelemetry.ErrorTypeOf(Kind);

    /// <inheritdoc />
    public override string ToString()
        => StatusCode is { } status
            ? $"{Kind.ToString()} ({status.ToString(CultureInfo.InvariantCulture)}): {Message}"
            : $"{Kind.ToString()}: {Message}";
}
