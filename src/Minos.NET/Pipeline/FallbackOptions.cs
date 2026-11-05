namespace Minos;

/// <summary>Which clients a fallback stage asks after its inner client fails, in what order, and what it names each in the path.</summary>
/// <remarks>
/// The settings are checked when a fallback stage is created from them, which throws <see cref="ArgumentException"/>
/// for one out of range; the stage copies them, so changing them afterwards has no effect.
/// </remarks>
public sealed class FallbackOptions
{
    /// <summary>
    /// Gets or sets the inner client's name in <see cref="DecisionResponse.Path"/>. Must not be empty or equal to a step's
    /// name. Defaults to <c>"primary"</c>.
    /// </summary>
    public string PrimaryName { get; set; } = "primary";

    /// <summary>
    /// Gets the steps asked, in order, after the inner client fails. At least one is required; names must be unique,
    /// compared ordinally.
    /// </summary>
    public IList<FallbackStep> Steps => StepList;

    // The concrete list behind Steps, for the stage's iteration without interface dispatch.
    internal List<FallbackStep> StepList { get; } = [];

    /// <summary>
    /// Gets or sets the time from the stage's entry to the deadline, used when the request has no
    /// <see cref="DecisionRequest.Deadline"/>; <see langword="null"/> for none. Must be positive when set. Defaults to <see langword="null"/>.
    /// </summary>
    public TimeSpan? Budget { get; set; }

    /// <summary>
    /// Gets or sets whether a failed call moves on to the next step; a failure it rejects is returned as is. A
    /// <see cref="DecisionErrorKind.Disposed"/> failure never moves on, whatever this returns, and is not passed to it. Must
    /// not be <see langword="null"/>. Defaults to <see cref="IsProviderFailure"/>.
    /// </summary>
    public Func<DecisionError, bool> ShouldFallBack { get; set; } = IsProviderFailure;

    /// <summary>
    /// Whether <paramref name="error"/> is a failure of the provider rather than of the request, so that another provider
    /// may still answer: <see cref="DecisionErrorKind.Unauthorized"/>, <see cref="DecisionErrorKind.RateLimited"/>,
    /// <see cref="DecisionErrorKind.Overloaded"/>, <see cref="DecisionErrorKind.Server"/>, <see cref="DecisionErrorKind.Http"/>,
    /// <see cref="DecisionErrorKind.Network"/>, <see cref="DecisionErrorKind.Timeout"/>,
    /// <see cref="DecisionErrorKind.InvalidResponse"/> and <see cref="DecisionErrorKind.Unsupported"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="DecisionErrorKind.Validation"/> and <see cref="DecisionErrorKind.InvalidQuestions"/> mean the request is
    /// wrong, so the next provider would reject it too; <see cref="DecisionErrorKind.Refused"/> is returned as is unless a
    /// custom <see cref="ShouldFallBack"/> opts in; <see cref="DecisionErrorKind.Disposed"/> means the app is shutting down.
    /// </remarks>
    /// <param name="error">The failed call's error.</param>
    /// <returns><see langword="true"/> when the chain should move on to the next step.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> is <see langword="null"/>.</exception>
    public static bool IsProviderFailure(DecisionError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Kind is DecisionErrorKind.Unauthorized
            or DecisionErrorKind.RateLimited
            or DecisionErrorKind.Overloaded
            or DecisionErrorKind.Server
            or DecisionErrorKind.Http
            or DecisionErrorKind.Network
            or DecisionErrorKind.Timeout
            or DecisionErrorKind.InvalidResponse
            or DecisionErrorKind.Unsupported;
    }

    internal FallbackOptions Clone()
    {
        var clone = new FallbackOptions
        {
            PrimaryName = PrimaryName,
            Budget = Budget,
            ShouldFallBack = ShouldFallBack,
        };
        clone.StepList.AddRange(StepList);
        return clone;
    }

    // The messages name the setting only, never a step's name: names are configuration, but an exception message
    // carries no value it was given.
    internal void Validate(string paramName)
    {
        if (string.IsNullOrEmpty(PrimaryName))
        {
            throw new ArgumentException("FallbackOptions.PrimaryName must not be null or empty.", paramName);
        }

        if (StepList.Count == 0)
        {
            throw new ArgumentException("FallbackOptions.Steps must hold at least one step.", paramName);
        }

        var names = new HashSet<string>(StringComparer.Ordinal) { PrimaryName };
        foreach (var step in StepList)
        {
            if (step is null)
            {
                throw new ArgumentException("Every entry in FallbackOptions.Steps must not be null.", paramName);
            }

            if (!names.Add(step.Name))
            {
                throw new ArgumentException("The names of FallbackOptions.Steps must be unique and differ from FallbackOptions.PrimaryName.", paramName);
            }

            if (step.ExpectedLatency < TimeSpan.Zero)
            {
                throw new ArgumentException("No step in FallbackOptions.Steps may have a negative ExpectedLatency.", paramName);
            }
        }

        if (Budget is { } budget && budget <= TimeSpan.Zero)
        {
            throw new ArgumentException("FallbackOptions.Budget must be positive when set.", paramName);
        }

        if (ShouldFallBack is null)
        {
            throw new ArgumentException("FallbackOptions.ShouldFallBack must not be null.", paramName);
        }
    }
}
