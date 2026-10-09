using Minos.Benchmarks.Compare.Adapters;

namespace Minos.Benchmarks.Compare;

/// <summary>
/// The benchmark workload every client sends, from <c>benchmarks/compare/workload/README.md</c>: one state, one Choice
/// and one Noul question with fixed keys, and the answers the mock's recorded response holds.
/// </summary>
public static class Workload
{
    /// <summary>The state every client sends; the mock ignores it.</summary>
    public const string State = "Is my booking to Rome still on? I fly tonight.";

    /// <summary>The model every client asks for: the default of every client that has one.</summary>
    public const string Model = "jev-latest";

    /// <summary>The key every client that takes one sends. It is never a real key.</summary>
    public const string DummyApiKey = "benchmark-dummy-key";

    /// <summary>The Choice question's key.</summary>
    public const string IntentKey = "intent";

    /// <summary>The Choice question's instructions.</summary>
    public const string IntentInstructions = "What does the traveller want?";

    /// <summary>The Noul question's key.</summary>
    public const string TravelsSoonKey = "travels_within24_hours";

    /// <summary>The Noul question's instructions.</summary>
    public const string TravelsSoonInstructions = "Does the request mention travelling within the next 24 hours?";

    /// <summary>The option the recorded answer chooses.</summary>
    public const string LookUpBooking = "look_up_booking";

    /// <summary>The Noul probability the recorded answer holds, which every measured call checks.</summary>
    public const double TravelsSoonNoul = 0.86;

    /// <summary>Gets the Choice options and their descriptions, in the order every client sends them.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> IntentOptions { get; } =
    [
        new(LookUpBooking, "Wants to see or look up an existing booking"),
        new("change_booking", "Wants to change dates, names, seats or luggage on a booking"),
        new("dispute_charge", "Disputes a charge or asks for money back"),
        new("other", "Anything else"),
    ];

    /// <summary>Gets the answers the mock's recorded response holds.</summary>
    public static WorkloadAnswers Expected { get; } = new(
        "typesafe/jev-1.13-20260917",
        LookUpBooking,
        0.99,
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [LookUpBooking] = 0.99,
            ["change_booking"] = 0,
            ["other"] = 0.01,
            ["dispute_charge"] = 0,
        },
        TravelsSoonNoul);
}
