using Minos.Conformance;
using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

public sealed class ConformanceTests : IDisposable
{
    private const string TypeSafe = "\"Provider\":\"TypeSafe\",\"ApiKey\":\"test-key\",\"Model\":\"jev-latest\"";

    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    private string[] Args(string providerBody, params string[] more)
        => ["conformance", "--config", _h.Config(providerBody), "--out", _h.Out, .. more];

    private ConformanceReport ReadReport() => ConformanceReport.FromJson(File.ReadAllText(Path.Combine(_h.Out, "conformance.json")));

    [Fact]
    public async Task Conformance_AgainstAConformingFake_ExitsZero_WritesBothFiles()
    {
        var fake = new FakeSystemOne { ReportedModel = "jev-1.13.0" };

        var run = await Invoke(fake, Args(TypeSafe));

        Assert.Equal(0, run.Exit);
        Assert.Equal("", run.Stderr);
        Assert.Contains(Path.Combine(_h.Out, "conformance.json"), run.Stdout, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_h.Out, "conformance.md"), run.Stdout, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_h.Out, "conformance.md")));
        var report = ReadReport();
        Assert.Equal(0, report.Failed);
        Assert.True(report.Passed > 0);
        Assert.Equal(ConformanceMode.Live, report.Mode);
    }

    [Fact]
    public async Task Conformance_PrintsTheProviderAndModelBeforeRunning()
    {
        var run = await Invoke(new FakeSystemOne { ReportedModel = "jev-1.13.0" }, Args(TypeSafe));

        var first = run.Stdout.Split('\n')[0];
        Assert.Contains("Fake", first, StringComparison.Ordinal);
        Assert.Contains("jev-latest", first, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conformance_FailingCheck_ExitsOne()
    {
        var fake = new FakeSystemOne { ReportedModel = "gpt-6-luna" };

        var run = await Invoke(fake, Args(TypeSafe));

        Assert.Equal(1, run.Exit);
        Assert.True(File.Exists(Path.Combine(_h.Out, "conformance.json")));
        Assert.All(ReadReport().FailedChecks, check => Assert.EndsWith(":R2", check, StringComparison.Ordinal));
        Assert.Contains("noul:R2", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conformance_ModelRulePerProvider()
    {
        var typeSafe = await Invoke(new FakeSystemOne { ReportedModel = "jev-1.13.0" }, Args(TypeSafe));
        Assert.Equal(0, typeSafe.Exit);
        var withRule = ReadReport();
        Assert.NotNull(withRule.ModelRule);
        Assert.Contains(withRule.Probes.SelectMany(p => p.Checks), c => string.Equals(c.Id, "R2", StringComparison.Ordinal) && c.Outcome == CheckOutcome.Pass);

        var custom = await Invoke(new FakeSystemOne { ReportedModel = "anything-else" }, Args("\"Provider\":\"Custom\",\"BaseAddress\":\"http://fake.local/\",\"ApiKey\":\"test-key\",\"Model\":\"fake-1\""));
        Assert.Equal(0, custom.Exit);
        var noRule = ReadReport();
        Assert.Null(noRule.ModelRule);
        Assert.All(noRule.Probes.SelectMany(p => p.Checks).Where(c => string.Equals(c.Id, "R2", StringComparison.Ordinal)), c => Assert.Equal(CheckOutcome.Skipped, c.Outcome));
    }

    [Fact]
    public async Task Conformance_KeylessCustom_ExitsZero_SkipsTheBadKeyCheck()
    {
        var run = await Invoke(
            new FakeSystemOne { ReportedModel = "anything" },
            Args("\"Provider\":\"Custom\",\"BaseAddress\":\"http://fake.local/\",\"Model\":\"fake-1\""));

        Assert.Equal(0, run.Exit);
        var e2 = ReadReport().Probes.Single(p => string.Equals(p.Probe, "bad-key", StringComparison.Ordinal)).Checks.Single(c => string.Equals(c.Id, "E2", StringComparison.Ordinal));
        Assert.Equal(CheckOutcome.Skipped, e2.Outcome);
    }

    [Fact]
    public async Task Conformance_OpenRouter_UsesItsRule()
    {
        var run = await Invoke(
            new FakeSystemOne { ReportedModel = "~typesafe/jev-latest" },
            Args("\"Provider\":\"OpenRouter\",\"ApiKey\":\"test-key\",\"Model\":\"jev-latest\""));

        Assert.Equal(0, run.Exit);
        Assert.Contains("typesafe/", ReadReport().ModelRule, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Conformance_ModelOption_OverridesTheConfiguredModel()
    {
        var fake = new FakeSystemOne { ReportedModel = "zed-1.0" };

        var run = await Invoke(fake, Args(TypeSafe, "--model", "zed-beta"));

        Assert.Equal(0, run.Exit);
        Assert.Equal("zed-beta", ReadReport().Model);
        Assert.All(fake.Bodies, b => Assert.Equal("zed-beta", b["model"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Conformance_NoModelAnywhere_UsesTheDefault()
    {
        var run = await Invoke(new FakeSystemOne { ReportedModel = "jev-1.13.0" }, Args("\"Provider\":\"TypeSafe\",\"ApiKey\":\"test-key\""));

        Assert.Equal(0, run.Exit);
        Assert.Equal(Minos.SystemOne.SystemOneDefaults.Model, ReadReport().Model);
    }

    [Fact]
    public async Task Conformance_UnknownProvider_ExitsTwo_SendsNothing()
    {
        var fake = new FakeSystemOne();

        var run = await Invoke(fake, Args(TypeSafe, "--provider", "Nope"));

        Assert.Equal(2, run.Exit);
        Assert.Equal(0, fake.Requests);
        Assert.False(Directory.Exists(_h.Out));
    }

    [Fact]
    public async Task Conformance_NetworkFault_ExitsOne_OneLine()
    {
        var run = await Invoke(new FakeSystemOne { ThrowFromRequest = 1, FaultMessage = "a\nb" }, Args(TypeSafe));

        Assert.Equal(1, run.Exit);
        Assert.Single(run.Stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }
}
