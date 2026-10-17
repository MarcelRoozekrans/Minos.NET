using System.IO.Compression;
using System.Xml.Linq;

namespace Minos.PackTests;

/// <summary>Packs the <c>Minos.NET.Tool</c> dotnet tool once, through <see cref="PackedProject"/>.</summary>
public sealed class ToolPackFixture : IDisposable
{
    private readonly PackedProject _packed = new("Minos.NET.Tool");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>Minos.NET.Tool.nupkg</c>.</summary>
[Collection(Packing.Name)]
public sealed class ToolPackageTests : IClassFixture<ToolPackFixture>
{
    // A tool's nuspec is written in the 2012/06 schema, the library packages' in 2013/05, so elements are matched by local name.
    private readonly ToolPackFixture _fixture;

    public ToolPackageTests(ToolPackFixture fixture) => _fixture = fixture;

    [Fact]
    public void IsADotnetToolPackage()
    {
        var packageType = Assert.Single(XDocument.Load(_fixture.NuspecPath).Descendants(), e => string.Equals(e.Name.LocalName, "packageType", StringComparison.Ordinal));

        Assert.Equal("DotnetTool", (string?)packageType.Attribute("name"));
    }

    [Fact]
    public void DeclaresTheMinosCommand()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var entry = Assert.Single(archive.Entries, e => string.Equals(e.FullName, "tools/net10.0/any/DotnetToolSettings.xml", StringComparison.Ordinal));
        using var stream = entry.Open();
        var command = XDocument.Load(stream).Descendants("Command").Single();

        Assert.Equal("minos", (string?)command.Attribute("Name"));
        Assert.Equal("Minos.NET.Tool.dll", (string?)command.Attribute("EntryPoint"));
    }

    [Fact]
    public void BundlesTheMinosAssembliesItUses()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.All(
            ["Minos.NET.Tool", "Minos.NET", "Minos.NET.SystemOne", "Minos.NET.Evaluation", "Minos.NET.Conformance"],
            assembly => Assert.Contains($"tools/net10.0/any/{assembly}.dll", names));
    }

    // The tool carries everything it uses, so a Minos package is never restored beside it.
    [Fact]
    public void DependsOnNoMinosPackage()
    {
        var dependencies = XDocument.Load(_fixture.NuspecPath).Descendants().Where(e => string.Equals(e.Name.LocalName, "dependency", StringComparison.Ordinal))
            .Select(dependency => (string?)dependency.Attribute("id"));

        Assert.DoesNotContain(dependencies, id => id is not null && id.StartsWith("Minos.NET", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipsItsOwnReadme_NotTheRootOne()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var entry = Assert.Single(archive.Entries, e => string.Equals(e.FullName, "README.md", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());

        Assert.StartsWith("# Minos.NET.Tool", reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
