using System.IO.Compression;
using System.Xml.Linq;

namespace Minos.PackTests;

/// <summary>Packs the shipping <c>Minos.NET.DependencyInjection</c> project once, through <see cref="PackedProject"/>.</summary>
public sealed class DependencyInjectionPackFixture : IDisposable
{
    private readonly PackedProject _packed = new("Minos.NET.DependencyInjection");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    /// <summary>Gets the version this fixture packs, which its <c>Minos.NET</c> dependency shares.</summary>
    public string PackageVersion => _packed.PackageVersion;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>Minos.NET.DependencyInjection.nupkg</c>.</summary>
[Collection(Packing.Name)]
public sealed class DependencyInjectionPackageTests : IClassFixture<DependencyInjectionPackFixture>
{
    private static readonly XNamespace Nuspec = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private readonly DependencyInjectionPackFixture _fixture;

    public DependencyInjectionPackageTests(DependencyInjectionPackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsTheLibraryAndItsDocs_AndNoOtherAssembly()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("lib/net10.0/Minos.NET.DependencyInjection.dll", names);
        Assert.Contains("lib/net10.0/Minos.NET.DependencyInjection.xml", names);
        Assert.Equal(
            ["lib/net10.0/Minos.NET.DependencyInjection.dll"],
            names.Where(name => name.EndsWith(".dll", StringComparison.Ordinal)).ToArray());
    }

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
    public void DependsOnTheCorePackageHttpAndOptionsConfigurationOnly()
    {
        var dependencies = Dependencies();

        Assert.Equal(
            ["Microsoft.Extensions.Http", "Microsoft.Extensions.Options.ConfigurationExtensions", "Minos.NET"],
            dependencies.Select(dependency => (string)dependency.Attribute("id")!).Order(StringComparer.Ordinal).ToArray());

        // The core package is packed at the same version; both Microsoft floors are the first .NET 10 release.
        Assert.Equal(_fixture.PackageVersion, Version(dependencies, "Minos.NET"));
        Assert.Equal("10.0.0", Version(dependencies, "Microsoft.Extensions.Http"));
        Assert.Equal("10.0.0", Version(dependencies, "Microsoft.Extensions.Options.ConfigurationExtensions"));
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
