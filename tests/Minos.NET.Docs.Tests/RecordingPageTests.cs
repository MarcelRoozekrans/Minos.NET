using System.Text.Json;
using Minos.Recording;

namespace Minos.Docs.Tests;

/// <summary>
/// The recording page's file example is read by the real reader with the result the page describes, and the page names
/// every log event and instrument the recorder emits.
/// </summary>
public sealed class RecordingPageTests
{
    private const string Page = "recording.md";
    private const string Marker = "<!-- decision record file -->";

    [Fact]
    public void TheFileExample_HasOneLineOfEachKind_AtVersion1()
    {
        var lines = Example().Split('\n');

        Assert.All(lines, line =>
        {
            using var document = JsonDocument.Parse(line);
            Assert.Equal(1, document.RootElement.GetProperty("version").GetInt32());
        });
        Assert.Equal(
            ["record", "outcome", "outcome", "record", "record", "erasure"],
            lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString()));
    }

    [Fact]
    public async Task TheFileExample_ReadsAsThePageSays()
    {
        var directory = Directory.CreateTempSubdirectory("minos-docs-recording-page-");
        try
        {
            var path = Path.Combine(directory.FullName, "decisions.jsonl");
            await File.WriteAllTextAsync(path, Example() + "\n");
            var problems = new List<DecisionRecordFileProblem>();
            var decisions = new List<RecordedDecision>();
            await foreach (var decision in DecisionRecordFile.ReadAsync(path, problems, CancellationToken.None))
            {
                decisions.Add(decision);
            }

            Assert.Empty(problems);

            // The last record is erased by its decision id; the first keeps its two outcomes, and the second is shadowed.
            Assert.Equal(2, decisions.Count);
            var kept = decisions[0];
            Assert.Equal("0199f1d2-7c4e-7a10-9b3e-2f6d8c1a4e55", kept.Record.DecisionId);
            Assert.Equal("customer-7", kept.Record.ErasureKey);
            Assert.Equal(2, kept.Record.Answers.Count);
            Assert.Null(kept.Record.Shadow);
            Assert.IsType<ConfirmedOutcome>(kept.Outcomes[0]);
            Assert.IsType<SignalOutcome>(kept.Outcomes[1]);

            var shadowed = decisions[1];
            Assert.Equal("0199f1d2-9e60-7c32-a05a-4b8f1e3c6a77", shadowed.Record.DecisionId);
            Assert.Empty(shadowed.Outcomes);
            var candidate = shadowed.Record.Shadow;
            Assert.NotNull(candidate);
            Assert.Equal("local", candidate.Provider);
            Assert.Equal("local-model-2", candidate.ServedModel);
            Assert.Null(candidate.ErrorKind);
            Assert.Equal(0.7, Assert.Single(candidate.Answers).Probability);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData("1009")]
    [InlineData("1010")]
    [InlineData("1011")]
    [InlineData("1012")]
    [InlineData("1013")]
    [InlineData("`minos.recording.records`")]
    [InlineData("`minos.recording.outcomes`")]
    [InlineData("`minos.recording.erasures`")]
    [InlineData("`minos.recording.shadows`")]
    [InlineData("`minos.recording.log_failures`")]
    [InlineData("`skipped_cap`")]
    [InlineData("`skipped_busy`")]
    [InlineData("`written`")]
    [InlineData("`dropped`")]
    [InlineData("`failed`")]
    public void ThePage_NamesEveryEventCounterAndResult(string name)
        => Assert.Contains(name, PageTables.Text(Page), StringComparison.Ordinal);

    // The body of the json code block that follows the marker comment.
    private static string Example()
    {
        var lines = PageTables.Text(Page).ReplaceLineEndings("\n").Split('\n');
        var at = Array.FindIndex(lines, line => string.Equals(line.TrimEnd(), Marker, StringComparison.Ordinal));
        Assert.True(at >= 0, $"{Page} has the marker {Marker}.");
        Assert.Equal("```json", lines[at + 1].TrimEnd());
        var end = Array.FindIndex(lines, at + 2, line => string.Equals(line.TrimEnd(), "```", StringComparison.Ordinal));
        Assert.True(end > 0, $"The block after {Marker} is closed.");
        return string.Join('\n', lines[(at + 2)..end]);
    }
}
