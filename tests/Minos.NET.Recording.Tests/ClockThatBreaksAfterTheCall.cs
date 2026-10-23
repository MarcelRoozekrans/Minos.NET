namespace Minos.Recording.Tests;

/// <summary>
/// A clock whose every second timestamp throws: the one the recording stage takes once the inner call has returned, so
/// making the record fails after the decision was made.
/// </summary>
internal sealed class ClockThatBreaksAfterTheCall(string message = "The clock broke.") : TimeProvider
{
    private int _reads;

    public override long GetTimestamp()
        => Interlocked.Increment(ref _reads) % 2 == 0 ? throw new InvalidCastException(message) : base.GetTimestamp();
}
