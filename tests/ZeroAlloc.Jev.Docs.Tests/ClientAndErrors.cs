namespace ZeroAlloc.Jev.Docs.Tests;

#region ClientAndErrors_Constructors
using Microsoft.Extensions.Logging;
using ZeroAlloc.Jev;

public static class ClientConstructors
{
    // The client creates its own HttpClient, and disposing the client disposes it.
    public static JevClient Owned(string apiKey)
        => new(new JevClientOptions { ApiKey = apiKey });

    // You pass an HttpClient you manage, for example one from IHttpClientFactory. The client never disposes it.
    public static JevClient Borrowed(HttpClient http, string apiKey)
        => new(http, new JevClientOptions { ApiKey = apiKey });

    // Either form also takes an ILoggerFactory, and then logs each operation and each retried attempt.
    public static JevClient Logged(string apiKey, ILoggerFactory loggers)
        => new(new JevClientOptions { ApiKey = apiKey }, loggers);
}
#endregion

public static class ClientOptions
{
    #region ClientAndErrors_Options
    // Every option set to the value it has when you leave it out, apart from the API key, which has no default:
    // without one, the client reads TYPESAFE_API_KEY, or OPENROUTER_API_KEY for OpenRouter.
    // BaseAddress is also left out: it defaults to the provider's own address.
    public static JevClientOptions SpelledOut(string apiKey)
        => new()
        {
            ApiKey = apiKey,
            Provider = JevProvider.TypeSafe,
            Model = "jev-latest",
            Timeout = TimeSpan.FromSeconds(60),
            MaxRetries = 2,
            InitialBackoff = TimeSpan.FromMilliseconds(500),
            MaxRetryDelay = TimeSpan.FromSeconds(30),
            Jitter = true,
        };
    #endregion

    #region ClientAndErrors_Validate
    // Validate runs the check the constructors run, without creating a client. It throws ArgumentException for a value
    // that is out of range, and InvalidOperationException when no API key can be found.
    public static string? Problem(JevClientOptions options)
    {
        try
        {
            options.Validate();
            return null;
        }
        catch (ArgumentException exception)
        {
            return exception.Message;
        }
        catch (InvalidOperationException exception)
        {
            return exception.Message;
        }
    }
    #endregion

    #region ClientAndErrors_Retries
    // Four retries, waiting about 1 s, 2 s, 4 s and then 8 s, each wait at most 20 s.
    public static JevClientOptions Patient(string apiKey)
        => new()
        {
            ApiKey = apiKey,
            MaxRetries = 4,
            InitialBackoff = TimeSpan.FromSeconds(1),
            MaxRetryDelay = TimeSpan.FromSeconds(20),
        };

    // No retries: a failed call is reported at once. Use this where a duplicate, billed request is worse than a failure.
    public static JevClientOptions NoRetries(string apiKey)
        => new() { ApiKey = apiKey, MaxRetries = 0 };
    #endregion

    #region ClientAndErrors_Timeouts
    // Each attempt may take 10 s, and there is one retry. The worst case for one call is therefore two attempts of
    // 10 s each, plus one wait of at most MaxRetryDelay between them.
    public static JevClientOptions Strict(string apiKey)
        => new() { ApiKey = apiKey, Timeout = TimeSpan.FromSeconds(10), MaxRetries = 1 };
    #endregion
}

public static class ClientFailures
{
    #region ClientAndErrors_Failures
    // Every failure of a call comes back as a JevError. Kind says what to do about it. The other members
    // add detail when there is some: StatusCode, RetryAfter, Detail and Exception.
    public static string Describe(JevError error) => error.Kind switch
    {
        JevErrorKind.Unauthorized => "Jev rejected the API key. Check the key and what it may access.",
        JevErrorKind.Validation => $"Jev rejected the request: {error.Detail?.GetRawText() ?? error.Message}",
        JevErrorKind.RateLimited or JevErrorKind.Overloaded when error.RetryAfter is { } wait
            => $"Jev is busy. It asks for {(int)wait.TotalMilliseconds} ms before the next call.",
        JevErrorKind.RateLimited or JevErrorKind.Overloaded => "Jev is busy. Try again later.",
        JevErrorKind.Server or JevErrorKind.Http => $"Jev failed with HTTP {error.StatusCode}: {error.Message}",
        JevErrorKind.Network or JevErrorKind.Timeout => $"Jev could not be reached: {error.Exception?.Message ?? error.Message}",
        JevErrorKind.InvalidResponse => $"Jev replied with something unreadable: {error.Message}",
        JevErrorKind.Unsupported => $"The provider cannot do that: {error.Message}",
        JevErrorKind.InvalidQuestions => $"The question set is invalid, {error.Failures.Count} rules broken.",
    JevErrorKind.Disposed => "The client was disposed while the call was running.",

        // A kind added in a later version still produces a useful message.
        _ => error.ToString(),
    };
    #endregion
}

public static class RawRequests
{
    #region ClientAndErrors_RawRequest
    // The raw API names its own model and questions, with ids you choose. Use it when the questions are not known at
    // compile time and the question set builder does not fit. Typed evaluation is shorter wherever it can be used.
    public static async Task<string> UrgencyAsync(IJevClient jev, string message, CancellationToken cancellationToken)
    {
        var result = await jev.EvaluateAsync(
            new SystemOneRequest
            {
                State = message,
                Questions = new Dictionary<string, JevQuestion>
                {
                    ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
                },
            },
            cancellationToken);

        if (result.IsFailure)
        {
            return ClientFailures.Describe(result.Error);
        }

        // Answers are keyed by the ids from the request, and each one is a NoulAnswer, ChoiceAnswer or ScoreAnswer.
        var response = result.Value;
        return response.Answers["is_urgent"] is NoulAnswer urgent
            ? $"{response.Model}: {urgent.Noul * 100:0} %, {response.Usage.InputTokens} input tokens"
            : "Unexpected answer type.";
    }
    #endregion
}

public static class ModelListing
{
    #region ClientAndErrors_Models
    public static async Task<string> ModelsAsync(IJevClient jev, CancellationToken cancellationToken)
    {
        var result = await jev.ListModelsAsync(cancellationToken);
        if (result.IsFailure)
        {
            return ClientFailures.Describe(result.Error);
        }

        var names = new List<string>();
        foreach (var model in result.Value.Models)
        {
            names.Add($"{model.Name} ({model.ReleaseDate})");
        }

        return string.Join(", ", names);
    }
    #endregion
}
