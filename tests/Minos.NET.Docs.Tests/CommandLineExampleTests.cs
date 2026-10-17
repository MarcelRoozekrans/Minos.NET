using Minos.SystemOne;
using Minos.Tool;

namespace Minos.Docs.Tests;

// Keeps the command line section of the Labelled datasets page in step with the tool: its minos.json example resolves for
// every provider it names, and its prices example loads. Each example follows a marker comment, so other json blocks on
// the page can come and go.
// Serialized with the other test classes that set or read the key environment variables.
[Collection("key environment")]
public sealed class CommandLineExampleTests
{
    private const string Page = "datasets.md";

    [Theory]
    [InlineData("TypeSafe", SystemOneProvider.TypeSafe, null)]
    [InlineData("OpenRouter", SystemOneProvider.OpenRouter, "cloudflare/clef-flash")]
    [InlineData("local", SystemOneProvider.Custom, "local-model")]
    public void TheConfigExample_Resolves(string name, SystemOneProvider provider, string? model)
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");
        var path = Write("minos.json", Example("<!-- minos.json -->"));

        var resolved = ToolConfiguration.Resolve(path, name, null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Equal(name, resolved.Value.Name);
        Assert.Equal(provider, resolved.Value.Options.Provider);
        Assert.Equal(model, resolved.Value.Options.Model);
    }

    [Fact]
    public void TheConfigExample_NamesTheActiveProvider()
    {
        using var environment = new KeyEnvironment("typesafe-key", "openrouter-key");
        var path = Write("minos.json", Example("<!-- minos.json -->"));

        var resolved = ToolConfiguration.Resolve(path, null, null);

        Assert.True(resolved.IsSuccess, resolved.IsFailure ? resolved.Error : string.Empty);
        Assert.Equal("TypeSafe", resolved.Value.Name);
    }

    [Fact]
    public void ThePricesExample_Loads()
    {
        var loaded = PricesFile.Load(Write("prices.json", Example("<!-- prices.json -->")));

        Assert.True(loaded.IsSuccess, loaded.IsFailure ? loaded.Error : string.Empty);
    }

    // The body of the json code block that follows the marker comment.
    private static string Example(string marker)
    {
        var lines = PageTables.Text(Page).ReplaceLineEndings("\n").Split('\n');
        var at = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), marker, StringComparison.Ordinal));
        Assert.True(at >= 0, $"{Page} has the marker {marker}.");
        Assert.Equal("```json", lines[at + 1].TrimEnd());
        var end = Array.FindIndex(lines, at + 2, line => string.Equals(line.TrimEnd(), "```", StringComparison.Ordinal));
        Assert.True(end > 0, $"The block after {marker} is closed.");
        return string.Join('\n', lines[(at + 2)..end]);
    }

    private static string Write(string file, string content)
    {
        var directory = Directory.CreateTempSubdirectory("minos-docs-").FullName;
        var path = Path.Combine(directory, file);
        File.WriteAllText(path, content);
        return path;
    }
}
