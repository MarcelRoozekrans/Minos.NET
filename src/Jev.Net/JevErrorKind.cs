namespace Jev.Net;

/// <summary>What went wrong in a failed Jev call.</summary>
public enum JevErrorKind
{
    /// <summary>The API key is missing, invalid or lacks access: HTTP 401 or 403.</summary>
    Unauthorized,

    /// <summary>The request was rejected as invalid: HTTP 400 or 422. <see cref="JevError.Detail"/> usually names the field.</summary>
    Validation,

    /// <summary>Too many requests: HTTP 429. Retry after <see cref="JevError.RetryAfter"/> when it is set.</summary>
    RateLimited,

    /// <summary>The service is overloaded: HTTP 529 or 503. Safe to retry with backoff.</summary>
    Overloaded,

    /// <summary>The service failed: any other HTTP 5xx status.</summary>
    Server,

    /// <summary>Any other unsuccessful HTTP status.</summary>
    Http,

    /// <summary>No response arrived: a DNS, connection or TLS failure.</summary>
    Network,

    /// <summary>The request exceeded the client time-out.</summary>
    Timeout,

    /// <summary>A successful response could not be read as the expected JSON.</summary>
    InvalidResponse,

    /// <summary>The operation is not available on the configured provider.</summary>
    Unsupported,
}
