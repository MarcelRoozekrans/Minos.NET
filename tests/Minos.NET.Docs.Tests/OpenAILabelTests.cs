using System.Xml.Linq;

namespace Minos.Docs.Tests;

// The OpenAI adapter is built from OpenAI's Decisions guide and has not run against the live API. Each place a user
// meets the package says so: the Providers page, the README's OpenAI part and the NuGet description.
public sealed class OpenAILabelTests
{
    private const string Label = "not yet tested against the live API";

    [Fact]
    public void The_providers_page_labels_the_OpenAI_client_unverified()
        => Assert.Contains(Label, Section(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", "providers.md")).ReplaceLineEndings("\n"), "## OpenAI"), StringComparison.Ordinal);

    [Fact]
    public void The_readme_labels_the_OpenAI_package_unverified()
    {
        var readme = File.ReadAllLines(Path.Combine(PublishedPages.Root, "README.md"));
        var install = Array.FindIndex(readme, line => line.Contains("dotnet add package Minos.NET.OpenAI", StringComparison.Ordinal));

        Assert.True(install > 0, "The README no longer installs Minos.NET.OpenAI.");
        // The label sits in the paragraph that introduces the install command.
        Assert.Contains(Label, string.Join('\n', readme[Math.Max(0, install - 4)..install]), StringComparison.Ordinal);
    }

    [Fact]
    public void The_package_description_labels_it_unverified()
    {
        var project = XDocument.Load(Path.Combine(PublishedPages.Root, "src", "Minos.NET.OpenAI", "Minos.NET.OpenAI.csproj"));
        var description = project.Descendants("Description").Single().Value;

        Assert.Contains(Label, description, StringComparison.Ordinal);
    }

    // The text from the heading to the next heading of the same level.
    private static string Section(string page, string heading)
    {
        var start = page.IndexOf("\n" + heading + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"The page has no '{heading}' section.");
        var next = page.IndexOf("\n## ", start + heading.Length + 1, StringComparison.Ordinal);
        return next < 0 ? page[start..] : page[start..next];
    }
}
