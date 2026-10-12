using Microsoft.Extensions.Configuration;

namespace Minos.Samples.Tests;

public sealed class SampleProvidersTests
{
    private const string Example = "Minos.NET.Samples.Example";

    [Fact]
    public void Names_AreTheProviderSections()
        => Assert.Equal(["Local", "TypeSafe"], SampleProviders.Names(Configuration("TypeSafe")));

    [Theory]
    [InlineData("TypeSafe")]
    [InlineData("typesafe")]
    public void Active_IsTheNamedSection_WhateverItsCase(string active)
    {
        var section = SampleProviders.Active(Configuration(active));

        Assert.Equal("TypeSafe", section.Key);
        Assert.Equal("TypeSafe", section["Provider"]);
    }

    [Fact]
    public void Active_RejectsAnUnknownProvider_AndNamesTheConfiguredOnes()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SampleProviders.Active(Configuration("OpenAI")));

        Assert.Contains("Unknown provider OpenAI", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Local, TypeSafe", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Active_RejectsAMissingActiveProvider_AndNamesTheConfiguredOnes(string? active)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => SampleProviders.Active(Configuration(active)));

        Assert.Contains("No provider is active", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Local, TypeSafe", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Active_SaysNone_WhenNoProviderIsConfigured()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection([new KeyValuePair<string, string?>(SampleProviders.ActiveKey, "Local")])
            .Build();

        var exception = Assert.Throws<InvalidOperationException>(() => SampleProviders.Active(configuration));

        Assert.Contains("The configured providers are: none.", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Arguments_None_AreReplayWithTheConfiguredProvider()
        => Assert.Equal(new SampleArguments(SampleMode.Replay, null), SampleArguments.Parse([]));

    [Theory]
    [InlineData(new[] { "--provider", "local" }, SampleMode.Replay, "local")]
    [InlineData(new[] { "--record", "--provider", "local" }, SampleMode.Record, "local")]
    [InlineData(new[] { "--provider", "openrouter", "--live" }, SampleMode.Live, "openrouter")]
    [InlineData(new[] { "--replay" }, SampleMode.Replay, null)]
    public void Arguments_ReadTheModeAndTheProvider_InAnyOrder(string[] args, SampleMode mode, string? provider)
        => Assert.Equal(new SampleArguments(mode, provider), SampleArguments.Parse(args));

    [Theory]
    [InlineData(new[] { "--provider" }, "--provider needs the name")]
    [InlineData(new[] { "--provider", "--live" }, "--provider needs the name")]
    [InlineData(new[] { "--live", "--record" }, "at most one of --replay")]
    [InlineData(new[] { "--provider", "a", "--provider", "b" }, "--provider at most once")]
    [InlineData(new[] { "--fast" }, "Unknown argument --fast")]
    [InlineData(new[] { "local" }, "Unknown argument local")]
    public void Arguments_RejectAnythingElse_SayingWhy(string[] args, string reason)
    {
        var exception = Assert.Throws<ArgumentException>(() => SampleArguments.Parse(args));

        Assert.Contains(reason, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderRecordingsPath_IsNull_ForLive()
        => Assert.Null(SampleHost.ProviderRecordingsPath(SampleMode.Live, "Minos.NET.Samples.ProviderSwitch", "Local"));

    [Theory]
    [InlineData(SampleMode.Replay)]
    [InlineData(SampleMode.Record)]
    public void ProviderRecordingsPath_IsTheProvidersFile_InLowerCase(SampleMode mode)
        => Assert.Equal(
            Path.Combine(Repository.Root, "samples", "Minos.NET.Samples.ProviderSwitch", "recordings", "openrouter.json"),
            SampleHost.ProviderRecordingsPath(mode, "Minos.NET.Samples.ProviderSwitch", "OpenRouter"));

    [Fact]
    public void RecordCommand_NamesTheProvider_InLowerCase()
    {
        Assert.Equal("dotnet run --project samples/" + Example + " -- --record", SampleHost.RecordCommand(Example));
        Assert.Equal(
            "dotnet run --project samples/" + Example + " -- --provider typesafe --record",
            SampleHost.RecordCommand(Example, "TypeSafe"));
    }

    private static IConfiguration Configuration(string? active)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Minos:Providers:TypeSafe:Provider"] = "TypeSafe",
                ["Minos:Providers:Local:Provider"] = "Custom",
                ["Minos:Active"] = active,
            })
            .Build();
}
