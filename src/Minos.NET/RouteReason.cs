namespace Minos;

/// <summary>Why a question was answered by the client that answered it, in a <see cref="QuestionRoute"/>.</summary>
public enum RouteReason
{
    /// <summary>The primary client's answer met its threshold, so the question was not re-asked.</summary>
    Confident = 0,

    /// <summary>The primary client's answer was below its threshold, and the target client answered the question.</summary>
    Escalated = 1,

    /// <summary>The primary client's answer was below its threshold, but too little time was left to ask the target, so the primary's answer stands.</summary>
    NoTimeLeft = 2,

    /// <summary>The primary client's answer was below its threshold, but the target failed or ran out of time, so the primary's answer stands.</summary>
    TargetFailed = 3,
}
