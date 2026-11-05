namespace Minos.Benchmarks.Compare.Adapters;

/// <summary>What a measured call reads from its answers: whether the traveller looks up a booking, and the Noul probability.</summary>
/// <param name="LooksUpBooking">Whether <c>intent</c> chose <c>look_up_booking</c>.</param>
/// <param name="TravelsSoon">The <c>travels_within24_hours</c> probability.</param>
public readonly record struct CallOutcome(bool LooksUpBooking, double TravelsSoon)
{
    /// <summary>Gets a value indicating whether the outcome matches the recorded answer.</summary>
    public bool IsExpected => LooksUpBooking && TravelsSoon == Workload.TravelsSoonNoul;
}
