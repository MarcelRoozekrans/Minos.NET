using System.IO.Compression;
using System.Xml.Linq;

namespace Minos.PackTests;

/// <summary>Packs the shipping <c>Minos.NET.Evaluation</c> project once, through <see cref="PackedProject"/>, with <c>Minos.NET</c> beside it at the same version.</summary>
public sealed class EvaluationPackFixture : IDisposable
{
    private readonly PackedProject _packed = new("Minos.NET.Evaluation", "Minos.NET");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    /// <summary>Gets the version this fixture packs, which its <c>Minos.NET</c> dependency shares.</summary>
    public string PackageVersion => _packed.PackageVersion;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>Minos.NET.Evaluation.nupkg</c>.</summary>
[Collection(Packing.Name)]
public sealed class EvaluationPackageTests : IClassFixture<EvaluationPackFixture>
{
    private static readonly XNamespace Nuspec = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";

    private readonly EvaluationPackFixture _fixture;

    public EvaluationPackageTests(EvaluationPackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsTheLibraryAndItsDocs_AndNoOtherAssembly()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var names = archive.Entries.Select(entry => entry.FullName).ToArray();

        Assert.Contains("lib/net10.0/Minos.NET.Evaluation.dll", names);
        Assert.Contains("lib/net10.0/Minos.NET.Evaluation.xml", names);
        Assert.Equal(
            ["lib/net10.0/Minos.NET.Evaluation.dll"],
            names.Where(name => name.EndsWith(".dll", StringComparison.Ordinal)).ToArray());
    }

    [Fact]
    public void ShipsItsOwnReadme_NotTheRootOne()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        var entry = Assert.Single(archive.Entries, e => string.Equals(e.FullName, "README.md", StringComparison.Ordinal));
        using var reader = new StreamReader(entry.Open());
        var readme = reader.ReadToEnd();

        Assert.StartsWith("# Minos.NET.Evaluation", readme, StringComparison.Ordinal);
        Assert.Equal("README.md", (string?)XDocument.Load(_fixture.NuspecPath).Root!.Element(Nuspec + "metadata")!.Element(Nuspec + "readme"));
    }

    [Fact]
    public void DependsOnMinosNet_AtThePackedVersion()
    {
        var group = XDocument.Load(_fixture.NuspecPath).Root!
            .Element(Nuspec + "metadata")!
            .Element(Nuspec + "dependencies")!
            .Elements(Nuspec + "group")
            .Single(g => string.Equals((string?)g.Attribute("targetFramework"), "net10.0", StringComparison.Ordinal));
        var dependencies = group.Elements(Nuspec + "dependency").ToArray();

        Assert.Equal(
            ["Minos.NET", "ZeroAlloc.Results"],
            dependencies.Select(dependency => (string)dependency.Attribute("id")!).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            _fixture.PackageVersion,
            (string?)dependencies.Single(dependency => string.Equals((string?)dependency.Attribute("id"), "Minos.NET", StringComparison.Ordinal)).Attribute("version"));
    }
}
