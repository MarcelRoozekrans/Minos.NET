namespace Minos.Recording;

/// <summary>
/// The daily cap and the running candidates of one <see cref="ShadowOptions"/>, shared by every stage built with it, so
/// the pipelines that shadow the same candidate share its cap and its limit on running calls.
/// </summary>
internal sealed class ShadowBudget(int dailyCap)
{
    private readonly Lock _gate = new();
    private DateTime _day;
    private int _used;
    private int _running;

    /// <summary>
    /// Checks the cap and takes a slot in one step. The count starts again when the UTC day moves forward; a clock that
    /// moves back counts against the latest day.
    /// </summary>
    /// <param name="time">The clock the day is read from.</param>
    /// <param name="day">The day the slot counts on, for <see cref="GiveCapBack"/>.</param>
    /// <returns><see langword="true"/> when a slot was taken.</returns>
    public bool TryTakeCap(TimeProvider time, out DateTime day)
    {
        lock (_gate)
        {
            // Read under the lock, so two callers around midnight never see the day move back.
            var today = time.GetUtcNow().UtcDateTime.Date;
            if (today > _day)
            {
                _day = today;
                _used = 0;
            }

            day = _day;
            if (_used >= dailyCap)
            {
                return false;
            }

            _used++;
            return true;
        }
    }

    /// <summary>Gives back a slot of a candidate that never started, unless its day is over.</summary>
    /// <param name="day">The day the slot was taken on.</param>
    public void GiveCapBack(DateTime day)
    {
        lock (_gate)
        {
            if (day == _day && _used > 0)
            {
                _used--;
            }
        }
    }

    /// <summary>Takes a running slot, unless <paramref name="maxConcurrent"/> candidates run.</summary>
    /// <param name="maxConcurrent">How many may run at once.</param>
    /// <returns><see langword="true"/> when a slot was taken.</returns>
    public bool TryTakeRunning(int maxConcurrent)
    {
        if (Interlocked.Increment(ref _running) <= maxConcurrent)
        {
            return true;
        }

        Interlocked.Decrement(ref _running);
        return false;
    }

    /// <summary>Frees a running slot.</summary>
    public void ReleaseRunning() => Interlocked.Decrement(ref _running);
}
