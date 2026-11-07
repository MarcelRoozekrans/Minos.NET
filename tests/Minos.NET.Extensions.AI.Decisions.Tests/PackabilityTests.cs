using System.Xml.Linq;

namespace Minos.Extensions.AI.Decisions.Tests;

/// <summary>
/// The decisions adapter follows an unreleased upstream API and is built against a local upstream DLL, so it must not be
/// packed or published before Phase 11.3 decides how it ships.
/// </summary>
public sealed class PackabilityTests
{
    private const string ProjectName = "Minos.NET.Extensions.AI.Decisions";

    [Fact]
    public void TheAdapterProject_IsNotPackable()
    {
        var project = XDocument.Load(ProjectFile());

        var values = project.Descendants("IsPackable").Select(e => e.Value.Trim()).ToList();

        Assert.NotEmpty(values);
        Assert.All(values, value => Assert.Equal("false", value));
    }

    [Fact]
    public void TheAdapterProject_DoesNotTurnPackingBackOnConditionally()
    {
        var project = XDocument.Load(ProjectFile());

        Assert.DoesNotContain(
            project.Descendants("IsPackable"),
            e => e.Attribute("Condition") is not null || e.Parent?.Attribute("Condition") is not null);
    }

    private static string ProjectFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Minos.NET.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", ProjectName, ProjectName + ".csproj");
    }
}
