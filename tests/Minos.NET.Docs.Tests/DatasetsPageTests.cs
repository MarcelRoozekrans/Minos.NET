using System.Text.RegularExpressions;
using Minos.Evaluation;

namespace Minos.Docs.Tests;

// Keeps the Labelled datasets page in step with the loader: its example line loads without a problem, its member table
// lists the members of an item line, and the README links it. It reads no other cell's wording.
public sealed partial class DatasetsPageTests
{
    private const string Page = "datasets.md";

    private static readonly string[] Members = ["id", "state", "questions", "expected", "source", "version"];

    [Fact]
    public void TheExampleLine_Loads()
    {
        var match = JsonBlock().Match(PageTables.Text(Page));
        Assert.True(match.Success, "The page has a json block.");

        var result = DecisionDataset.Parse(match.Groups["json"].Value);

        Assert.True(result.IsSuccess, result.IsFailure ? string.Join('\n', result.Error.Select(problem => problem.ToString())) : string.Empty);
        Assert.Single(result.Value);
    }

    [Fact]
    public void TheMemberTable_ListsEveryMember()
        => Assert.Equal(Members, PageTables.Rows(Page, "The members").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void ThePage_IsInTheReadme()
    {
        var readme = File.ReadAllText(Path.Combine(PublishedPages.Root, "README.md"));

        Assert.Contains("(https://marcelroozekrans.github.io/Minos.NET/datasets)", readme, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"```json\r?\n(?<json>.*?)\r?\n```", RegexOptions.Singleline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JsonBlock();
}
