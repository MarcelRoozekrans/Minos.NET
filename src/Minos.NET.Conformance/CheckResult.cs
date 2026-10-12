using System.Text.Json.Serialization;

namespace Minos.Conformance;

/// <summary>A check's outcome. A check that cannot run is skipped, never passed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckOutcome>))]
public enum CheckOutcome
{
    /// <summary>The endpoint and Minos behaved as the check requires.</summary>
    Pass,

    /// <summary>They did not; <see cref="CheckResult.Detail"/> says how.</summary>
    Fail,

    /// <summary>The check could not run; <see cref="CheckResult.Detail"/> says why.</summary>
    Skipped,
}

/// <summary>One check's result: its id, such as <c>S1</c>, its outcome and a one-line detail.</summary>
/// <param name="Id">The check's id.</param>
/// <param name="Outcome">The outcome.</param>
/// <param name="Detail">What passed, what failed, or why it was skipped.</param>
public sealed record CheckResult(string Id, CheckOutcome Outcome, string Detail)
{
    /// <summary>A passing result.</summary>
    /// <param name="id">The check's id.</param>
    /// <param name="detail">What passed.</param>
    /// <returns>The result.</returns>
    public static CheckResult Passed(string id, string detail) => new(id, CheckOutcome.Pass, detail);

    /// <summary>A failing result.</summary>
    /// <param name="id">The check's id.</param>
    /// <param name="detail">What failed.</param>
    /// <returns>The result.</returns>
    public static CheckResult Failed(string id, string detail) => new(id, CheckOutcome.Fail, detail);

    /// <summary>A skipped result.</summary>
    /// <param name="id">The check's id.</param>
    /// <param name="reason">Why it could not run.</param>
    /// <returns>The result.</returns>
    public static CheckResult Skip(string id, string reason) => new(id, CheckOutcome.Skipped, reason);
}

/// <summary>One probe's results, or why the whole probe was skipped.</summary>
/// <param name="Probe">The probe's id.</param>
/// <param name="Skipped">Why the probe did not run, or <see langword="null"/> when it ran.</param>
/// <param name="Checks">Its checks' results, in check order; each skipped, with <paramref name="Skipped"/>, when the probe did not run.</param>
public sealed record ProbeReport(string Probe, string? Skipped, IReadOnlyList<CheckResult> Checks);
