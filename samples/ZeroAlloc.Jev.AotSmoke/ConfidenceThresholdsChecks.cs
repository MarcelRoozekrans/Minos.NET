namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="ConfidenceThresholds"/>' defaults, validation and equality under Native AOT.</summary>
internal static class ConfidenceThresholdsChecks
{
    [Covers("ZeroAlloc.Jev.ConfidenceThresholds.ConfidenceThresholds() -> void")]
    [Covers("ZeroAlloc.Jev.ConfidenceThresholds.Equals(ZeroAlloc.Jev.ConfidenceThresholds other) -> bool")]
    public static void DefaultThresholdsEqualTheirExplicitValues()
    {
        var defaults = new ConfidenceThresholds();

        Program.Check(
            defaults.Medium == 0.5
                && defaults.High == 0.9
                && defaults.Equals(ConfidenceThresholds.Default)
                && defaults.Equals(new ConfidenceThresholds(0.5, 0.9))
                && !defaults.Equals(new ConfidenceThresholds(0.6, 0.9)),
            "new ConfidenceThresholds() is 0.5 and 0.9, equal to the same explicit thresholds, under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentOutOfRangeException>(() => _ = new ConfidenceThresholds(0.9, 0.5))
                && SmokeAssert.Throws<ArgumentOutOfRangeException>(() => _ = new ConfidenceThresholds(double.NaN, 0.9)),
            "ConfidenceThresholds rejects a medium above high, and NaN, under Native AOT");
    }
}
