namespace Minos;

/// <summary>What went wrong in a failed call.</summary>
public enum DecisionErrorKind
{
    /// <summary>The API key is missing, invalid or lacks access: HTTP 401 or 403.</summary>
    Unauthorized = 1,

    /// <summary>The request was rejected as invalid: HTTP 400 or 422. <see cref="DecisionError.Detail"/> usually names the field.</summary>
    Validation = 2,

    /// <summary>
    /// Too many requests: HTTP 429, or a call the client's own rate limiter rejected before sending it, such as the
    /// <c>UseRateLimiting</c> stage in <c>Minos.NET.RateLimiting</c>, whose <see cref="DecisionError.StatusCode"/> is
    /// <see langword="null"/>. A retry stage, such as the one in a client's standard pipeline, retries a 429 by default
    /// (<see cref="DecisionRetryOptions.IsTransient"/>), honouring <see cref="DecisionError.RetryAfter"/> when it is set, and
    /// returns it only after its retries are used up. A local rejection is retried only by a retry stage added outside the rate limiter.
    /// </summary>
    RateLimited = 3,

    /// <summary>
    /// The service is overloaded: HTTP 529 or 503. A retry stage, such as the one in a client's standard pipeline, retries this by default (<see cref="DecisionRetryOptions.IsTransient"/>),
    /// and returns it only after its retries are used up.
    /// </summary>
    Overloaded = 4,

    /// <summary>
    /// The service failed: any other HTTP 5xx status. A retry stage, such as the one in a client's standard pipeline, retries this by default (<see cref="DecisionRetryOptions.IsTransient"/>),
    /// and returns it only after its retries are used up.
    /// </summary>
    Server = 5,

    /// <summary>
    /// Any other unsuccessful HTTP status. For a 408, a retry stage, such as the one in a client's standard pipeline, retries this by default (<see cref="DecisionRetryOptions.IsTransient"/>),
    /// and returns it only after its retries are used up.
    /// </summary>
    Http = 6,

    /// <summary>
    /// No response arrived: a DNS, connection or TLS failure. A retry stage, such as the one in a client's standard pipeline, retries this by default (<see cref="DecisionRetryOptions.IsTransient"/>),
    /// and returns it only after its retries are used up.
    /// </summary>
    Network = 7,

    /// <summary>
    /// The request exceeded the client time-out. A retry stage, such as the one in a client's standard pipeline, retries this by default (<see cref="DecisionRetryOptions.IsTransient"/>),
    /// and returns it only after its retries are used up.
    /// </summary>
    Timeout = 8,

    /// <summary>A successful response could not be read as the expected JSON.</summary>
    InvalidResponse = 9,

    /// <summary>
    /// The operation is not available on the configured provider. A question set the provider's capabilities do not
    /// accept is this kind too, and <see cref="DecisionError.Failures"/> lists each capability failure.
    /// </summary>
    Unsupported = 10,

    /// <summary>
    /// A question set built with <see cref="QuestionSetBuilder"/> breaks the API's rules; <see cref="DecisionError.Failures"/>
    /// lists each one. <c>Build</c> returns it, and no request is sent.
    /// </summary>
    InvalidQuestions = 11,

    /// <summary>
    /// The client was disposed while the call was in flight: either the disposal tore its request down, or a retry was
    /// due and the client did not send it, because a disposed client never retries. A call started after disposal
    /// throws <see cref="ObjectDisposedException"/> instead.
    /// </summary>
    Disposed = 12,

    /// <summary>
    /// The model refused to answer one or more questions. <see cref="DecisionError.Failures"/> names each refused question,
    /// with rule <c>refusal</c>. It is not retried by default: <see cref="DecisionRetryOptions.IsTransient"/> excludes it,
    /// though a custom <see cref="DecisionRetryOptions.ShouldRetry"/> can retry it. OpenAI's Decisions API reports
    /// refusals; System One providers do not.
    /// </summary>
    Refused = 13,
}
