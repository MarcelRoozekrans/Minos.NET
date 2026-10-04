namespace ZeroAlloc.Jev;

/// <summary>What went wrong in a failed Jev call.</summary>
public enum JevErrorKind
{
    /// <summary>The API key is missing, invalid or lacks access: HTTP 401 or 403.</summary>
    Unauthorized = 1,

    /// <summary>The request was rejected as invalid: HTTP 400 or 422. <see cref="JevError.Detail"/> usually names the field.</summary>
    Validation = 2,

    /// <summary>
    /// Too many requests: HTTP 429. The client already retries this, honouring <see cref="JevError.RetryAfter"/> when
    /// it is set, and returns it only after <see cref="JevClientOptions.MaxRetries"/> retries are used up.
    /// </summary>
    RateLimited = 3,

    /// <summary>
    /// The service is overloaded: HTTP 529 or 503. The client already retries this with backoff, and returns it only
    /// after <see cref="JevClientOptions.MaxRetries"/> retries are used up.
    /// </summary>
    Overloaded = 4,

    /// <summary>
    /// The service failed: any other HTTP 5xx status. The client already retries this with backoff, and returns it
    /// only after <see cref="JevClientOptions.MaxRetries"/> retries are used up.
    /// </summary>
    Server = 5,

    /// <summary>Any other unsuccessful HTTP status.</summary>
    Http = 6,

    /// <summary>
    /// No response arrived: a DNS, connection or TLS failure. The client already retries this with backoff, and
    /// returns it only after <see cref="JevClientOptions.MaxRetries"/> retries are used up.
    /// </summary>
    Network = 7,

    /// <summary>
    /// The request exceeded the client time-out. The client already retries this with backoff, and returns it only
    /// after <see cref="JevClientOptions.MaxRetries"/> retries are used up.
    /// </summary>
    Timeout = 8,

    /// <summary>A successful response could not be read as the expected JSON.</summary>
    InvalidResponse = 9,

    /// <summary>The operation is not available on the configured provider.</summary>
    Unsupported = 10,

    /// <summary>
    /// A question set built with <see cref="JevQuestionSetBuilder"/> breaks the API's rules; <see cref="JevError.Failures"/>
    /// lists each one. <c>Build</c> returns it, and no request is sent.
    /// </summary>
    InvalidQuestions = 11,

    /// <summary>
    /// The client was disposed while the call was in flight: either the disposal tore its request down, or a retry was
    /// due and the client did not send it, because a disposed client never retries. A call started after disposal
    /// throws <see cref="ObjectDisposedException"/> instead.
    /// </summary>
    Disposed = 12,
}
