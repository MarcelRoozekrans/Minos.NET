using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace Minos.PackTests;

/// <summary>
/// Packs the shipping <c>Minos.NET.SystemOne</c> project once, through <see cref="PackedProject"/>, with <c>Minos.NET</c>
/// and <c>Minos.NET.DependencyInjection</c> beside it at the same version for the packed consumer.
/// </summary>
public sealed class SystemOnePackFixture : IDisposable
{
    private readonly PackedProject _packed = new("Minos.NET.SystemOne", "Minos.NET", "Minos.NET.DependencyInjection");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    /// <summary>Gets the version this fixture packs, which its <c>Minos.NET</c> dependency shares.</summary>
    public string PackageVersion => _packed.PackageVersion;

    /// <summary>Gets the directory holding the packed <c>.nupkg</c> files, usable as a local package source.</summary>
    public string OutputDirectory => _packed.OutputDirectory;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>Minos.NET.SystemOne.nupkg</c>.</summary>
[Collection(Packing.Name)]
public sealed class SystemOnePackageTests : IClassFixture<SystemOnePackFixture>
{
    private static readonly XNamespace Nuspec = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private readonly SystemOnePackFixture _fixture;

    public SystemOnePackageTests(SystemOnePackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsTheLibraryAndItsDocs_AndNoOtherAssembly()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("lib/net10.0/Minos.NET.SystemOne.dll", names);
        Assert.Contains("lib/net10.0/Minos.NET.SystemOne.xml", names);
        Assert.Equal(
            ["lib/net10.0/Minos.NET.SystemOne.dll"],
            names.Where(name => name.EndsWith(".dll", StringComparison.Ordinal)).ToArray());
    }

    // The generator, analyzers and code fixes ship once, in the core package.
    [Fact]
    public void ShipsNoAnalyzersOrBuildAssets()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);

        Assert.DoesNotContain(
            archive.Entries,
            entry => entry.FullName.StartsWith("analyzers/", StringComparison.Ordinal)
                || entry.FullName.StartsWith("build/", StringComparison.Ordinal)
                || entry.FullName.StartsWith("buildTransitive/", StringComparison.Ordinal));
    }

    [Fact]
    public void DependsOnTheMinosPackagesAndTheirRuntimePackagesOnly()
    {
        var dependencies = Dependencies();

        Assert.Equal(
            [
                "Microsoft.Extensions.Http",
                "Microsoft.Extensions.Logging.Abstractions",
                "Microsoft.Extensions.Options.ConfigurationExtensions",
                "Minos.NET",
                "Minos.NET.DependencyInjection",
                "ZeroAlloc.Rest",
                "ZeroAlloc.Rest.SystemTextJson",
                "ZeroAlloc.Results",
                "ZeroAlloc.Telemetry",
            ],
            dependencies.Select(dependency => (string)dependency.Attribute("id")!).Order(StringComparer.Ordinal).ToArray());

        // The Minos packages are packed at the same version; release-please versions every package together. The
        // Microsoft floors are the first .NET 10 release.
        Assert.Equal(_fixture.PackageVersion, Version(dependencies, "Minos.NET"));
        Assert.Equal(_fixture.PackageVersion, Version(dependencies, "Minos.NET.DependencyInjection"));
        Assert.Equal("10.0.0", Version(dependencies, "Microsoft.Extensions.Logging.Abstractions"));
        Assert.Equal("10.0.0", Version(dependencies, "Microsoft.Extensions.Http"));
        Assert.Equal("10.0.0", Version(dependencies, "Microsoft.Extensions.Options.ConfigurationExtensions"));
    }

    [Fact]
    public void TelemetryDependency_IsTheFloor_AndExcludesBuildAndAnalyzers()
    {
        var dependency = PackageContentTests.AssertExcludesBuildAndAnalyzers(_fixture.NuspecPath, "ZeroAlloc.Telemetry");

        Assert.Equal("1.11.0", (string?)dependency.Attribute("version"));
    }

    // The shared metadata, with the protocol package's own tags after the shared ones: the vendor tags live here, not
    // in the provider-neutral core.
    [Fact]
    public void CarriesTheMinosMetadata_AndItsOwnTags()
    {
        var metadata = XDocument.Load(_fixture.NuspecPath).Root!.Element(Nuspec + "metadata")!;

        Assert.Equal("https://marcelroozekrans.github.io/Minos.NET/", (string?)metadata.Element(Nuspec + "projectUrl"));
        Assert.Equal("https://github.com/MarcelRoozekrans/Minos.NET", (string?)metadata.Element(Nuspec + "repository")!.Attribute("url"));
        Assert.Equal("Copyright (c) 2026 Marcel Roozekrans", (string?)metadata.Element(Nuspec + "copyright"));
        Assert.Equal("icon.png", (string?)metadata.Element(Nuspec + "icon"));
        Assert.Equal(PackageContentTests.PackageTags + " systemone jev typesafe openrouter vllm", (string?)metadata.Element(Nuspec + "tags"));
    }

    // The package's own ZeroAlloc.Telemetry reference carries the same PrivateAssets as the core's, and NuGet still flows
    // the proxy generator to consumers, NuGet/Home#6720. A consumer that references only this package and has no
    // [Instrument] interface must build clean and get no source from it.
    [Fact]
    public void PackedConsumer_WithoutInstrumentTypes_BuildsCleanAndGetsNoSourceFromTheTelemetryGenerator()
    {
        var consumer = Path.Combine(_fixture.OutputDirectory, "consumer");
        Directory.CreateDirectory(consumer);
        var machinePackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        File.WriteAllText(Path.Combine(consumer, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{Path.Combine(consumer, "packages")}" />
              </config>
              <packageSources>
                <clear />
                <add key="packed" value="{_fixture.OutputDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <fallbackPackageFolders>
                <add key="machine" value="{machinePackages}" />
              </fallbackPackageFolders>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Minos.NET.SystemOne" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(consumer, "Client.cs"), """
            using Minos.SystemOne;

            namespace Consumer;

            public static class Client
            {
                public static SystemOneClient Create() => new(new SystemOneClientOptions { ApiKey = "consumer-key" });
            }
            """);

        PackageContentTests.RunDotnet(consumer, "restore", "Consumer.csproj");
        var listing = PackageContentTests.RunDotnet(consumer, "msbuild", "Consumer.csproj", "-t:ResolveLockFileAnalyzers", "-getItem:Analyzer");

        using var output = JsonDocument.Parse(listing);
        var analyzers = output.RootElement.GetProperty("Items").GetProperty("Analyzer").EnumerateArray()
            .Select(item => Path.GetFileName(item.GetProperty("Identity").GetString()!))
            .ToArray();

        // Non-vacuity guard: the generator reaches the consumer, so the check below proves it inert rather than absent.
        Assert.Contains("ZeroAlloc.Telemetry.Generator.dll", analyzers);

        PackageContentTests.RunDotnet(consumer, "build", "Consumer.csproj", "-c", "Release", "-p:EmitCompilerGeneratedFiles=true", "-warnaserror");

        // Each generated file sits in obj/.../generated/{generator assembly}/{generator}/: name its generator assembly.
        var generators = Directory.GetFiles(Path.Combine(consumer, "obj"), "*.cs", SearchOption.AllDirectories)
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file))!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(generators, name => name.StartsWith("ZeroAlloc.Telemetry", StringComparison.Ordinal));
    }

    // The net10.0 group's dependencies.
    private XElement[] Dependencies()
    {
        var nuspec = XDocument.Load(_fixture.NuspecPath);
        var group = nuspec.Root!
            .Element(Nuspec + "metadata")!
            .Element(Nuspec + "dependencies")!
            .Elements(Nuspec + "group")
            .FirstOrDefault(g => string.Equals((string?)g.Attribute("targetFramework"), "net10.0", StringComparison.Ordinal));
        Assert.True(group is not null, "The nuspec has no net10.0 dependency group.");
        return [.. group.Elements(Nuspec + "dependency")];
    }

    private static string? Version(XElement[] dependencies, string id)
        => (string?)Array.Find(dependencies, dependency => string.Equals((string?)dependency.Attribute("id"), id, StringComparison.Ordinal))?.Attribute("version");
}
