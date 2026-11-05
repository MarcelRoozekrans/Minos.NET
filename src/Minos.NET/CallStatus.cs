namespace Minos;

/// <summary>What became of one call a routing stage made or skipped, in a <see cref="DecisionCall"/>.</summary>
public enum CallStatus
{
    /// <summary>The client was called and answered.</summary>
    Answered = 0,

    /// <summary>The client was called and failed; <see cref="DecisionCall.ErrorKind"/> says how.</summary>
    Failed = 1,

    /// <summary>The client was not called, because less time was left before the deadline than it was expected to take.</summary>
    NotCalledNoTimeLeft = 2,
}
