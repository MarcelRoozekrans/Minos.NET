using System.IO.Compression;
using System.Xml.Linq;

namespace Minos.PackTests;

/// <summary>Packs the shipping <c>Minos.NET.RateLimiting</c> project once, through <see cref="PackedProject"/>, with <c>Minos.NET</c> beside it at the same version for the packed consumer.</summary>
public sealed class RateLimitingPackFixture : IDisposable
{
    private readonly PackedProject _packed = new("Minos.NET.RateLimiting", "Minos.NET");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    /// <summary>Gets the version this fixture packs, which its <c>Minos.NET</c> dependency shares.</summary>
    public string PackageVersion => _packed.PackageVersion;

    /// <summary>Gets the directory holding the packed <c>.nupkg</c> files, usable as a local package source.</summary>
    public string OutputDirectory => _packed.OutputDirectory;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>Minos.NET.RateLimiting.nupkg</c>.</summary>
[Collection(Packing.Name)]
public sealed class RateLimitingPackageTests : IClassFixture<RateLimitingPackFixture>
{
    private static readonly XNamespace Nuspec = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private readonly RateLimitingPackFixture _fixture;

    public RateLimitingPackageTests(RateLimitingPackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsTheLibraryAndItsDocs_AndNoOtherAssembly()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("lib/net10.0/Minos.NET.RateLimiting.dll", names);
        Assert.Contains("lib/net10.0/Minos.NET.RateLimiting.xml", names);
        Assert.Equal(
            ["lib/net10.0/Minos.NET.RateLimiting.dll"],
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
    public void DependsOnMinosNetAndRateLimitingOnly()
    {
        var dependencies = Dependencies();

        Assert.Equal(
            [
                "Minos.NET",
                "System.Threading.RateLimiting",
                "ZeroAlloc.Results",
            ],
            dependencies.Select(dependency => (string)dependency.Attribute("id")!).Order(StringComparer.Ordinal).ToArray());

        // Minos.NET is packed at the same version; release-please versions every package together. The Microsoft
        // floor is the first .NET 10 release.
        Assert.Equal(_fixture.PackageVersion, Version(dependencies, "Minos.NET"));
        Assert.Equal("10.0.0", Version(dependencies, "System.Threading.RateLimiting"));
    }

    [Fact]
    public void CarriesTheMinosMetadata_AndItsOwnTags()
    {
        var metadata = XDocument.Load(_fixture.NuspecPath).Root!.Element(Nuspec + "metadata")!;

        Assert.Equal("https://marcelroozekrans.github.io/Minos.NET/", (string?)metadata.Element(Nuspec + "projectUrl"));
        Assert.Equal("https://github.com/MarcelRoozekrans/Minos.NET", (string?)metadata.Element(Nuspec + "repository")!.Attribute("url"));
        Assert.Equal("Copyright (c) 2026 Marcel Roozekrans", (string?)metadata.Element(Nuspec + "copyright"));
        Assert.Equal("icon.png", (string?)metadata.Element(Nuspec + "icon"));
        Assert.Equal(PackageContentTests.PackageTags + " rate-limiting", (string?)metadata.Element(Nuspec + "tags"));
    }

    [Fact]
    public void PackedConsumer_CompilesTheRateLimitingStage()
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
                <PackageReference Include="Minos.NET.RateLimiting" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(consumer, "Client.cs"), """
            using System.Threading.RateLimiting;
            using Minos;

            namespace Consumer;

            public static class Client
            {
                public static IDecisionClient Wrap(IDecisionClient inner, RateLimiter limiter) => inner.AsBuilder().UseRateLimiting(limiter).Build();
            }
            """);

        PackageContentTests.RunDotnet(consumer, "restore", "Consumer.csproj");
        PackageContentTests.RunDotnet(consumer, "build", "Consumer.csproj", "-c", "Release", "-warnaserror");
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
