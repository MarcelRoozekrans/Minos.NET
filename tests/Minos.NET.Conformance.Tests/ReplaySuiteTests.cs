namespace Minos.Conformance.Tests;

/// <summary>
/// The suite over each target's fixture, offline: no check may fail, and exactly the expected checks are skipped, so a
/// check that starts skipping fails here instead of passing quietly.
/// </summary>
public sealed class ReplaySuiteTests
{
    private static readonly Dictionary<string, string[]> ExpectedSkips = new(StringComparer.Ordinal)
    {
        // A Noul has no options, levels or confidence, and a Choice probe asks no Score question; no 429 was met.
        ["typesafe"] = ["noul:D2", "noul:D3", "noul:D4", "choice:D3", "run:E4"],

        // As TypeSafe's, and OpenRouter's 400 and 401 carry no request-id header.
        ["openrouter"] = ["noul:D2", "noul:D3", "noul:D4", "choice:D3", "invalid:E3", "bad-key:E3", "run:E4"],

        // The guide's examples name no model and no usage, and OpenAI states no model rule yet. The invalid probe has no
        // recorded body, so E1 and E3 skip; the bad-key x-request-id value was dropped, so its E3 skips; no 429 was met, so E4 skips.
        ["openai"] =
        [
            "noul:D2", "noul:D3", "noul:D4", "noul:R1", "noul:R2", "noul:R3",
            "choice:D3", "choice:R1", "choice:R2", "choice:R3",
            "score:R1", "score:R2", "score:R3",
            "mixed:R1", "mixed:R2", "mixed:R3",
            "keyed:R1", "keyed:R2", "keyed:R3",
            "invalid:E1", "invalid:E3", "bad-key:E3", "run:E4",
        ],
    };

    public static TheoryData<string> Targets => new() { "typesafe", "openrouter", "openai" };

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Replay_FailsNoCheck(string target)
    {
        var report = await new ConformanceRunner().RunAsync(Fixtures.Replay(target), ConformanceMode.Replay, Fixtures.Path(target));

        Assert.True(report.Failed == 0, report.ToMarkdown());
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Replay_SkipsExactlyTheExpectedChecks(string target)
    {
        var report = await new ConformanceRunner().RunAsync(Fixtures.Replay(target), ConformanceMode.Replay, Fixtures.Path(target));

        Assert.Equal(ExpectedSkips[target], report.SkippedChecks);
    }

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task Replay_RunsEveryProbe_AndReportsTheFixturesSource(string target)
    {
        var fixture = ConformanceFixture.Load(Fixtures.Path(target));

        var report = await new ConformanceRunner().RunAsync(Fixtures.Replay(target), ConformanceMode.Replay, Fixtures.Path(target));

        Assert.Equal(ConformanceProbe.All.Select(probe => probe.Id), report.Probes.Select(probe => probe.Probe));
        Assert.Equal(fixture.Source, report.FixtureSource);
        Assert.Equal(ConformanceMode.Replay, report.Mode);
    }

    [Fact]
    public async Task Replay_AProbeTheFixtureSkips_IsSkippedWithItsReason()
    {
        var report = await new ConformanceRunner().RunAsync(Fixtures.Replay("openai"), ConformanceMode.Replay, Fixtures.Path("openai"));

        var invalid = report.Probes.First(probe => string.Equals(probe.Probe, "invalid", StringComparison.Ordinal));
        Assert.Equal("The fixture skips it: No invalid-request body from OpenAI exists yet; the guide documents none, and there is no key to record one.", invalid.Skipped);
        Assert.All(invalid.Checks, check => Assert.Equal(CheckOutcome.Skipped, check.Outcome));
    }
}
