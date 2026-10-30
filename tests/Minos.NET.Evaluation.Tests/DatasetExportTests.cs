using System.Runtime.CompilerServices;
using System.Text.Json;
using Minos.Recording;
using static Minos.Evaluation.Tests.RecordLines;

namespace Minos.Evaluation.Tests;

public sealed class DatasetExportTests
{
    private static string At(string time) => "2026-10-22T" + time + ":00+00:00";

    private static DecisionDataset Load(string dataset)
    {
        var result = DecisionDataset.Parse(dataset);
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    [Fact]
    public async Task OneConfirmedDecision_BecomesOneItem()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"queue":"technical","urgent":true}"""));

        var item = Assert.Single(Load(dataset));
        Assert.Equal("d1", item.Id);
        Assert.True(item.State.TryGetString(out var state));
        Assert.Equal("The app crashes when I pay.", state);
        Assert.Equal(["urgent", "queue", "mood"], item.Definition.Questions.Select(q => q.Key));
        Assert.Equal(ExpectedAnswer.ForChoice("technical"), item.Expected["queue"]);
        Assert.Equal(ExpectedAnswer.ForNoul(true), item.Expected["urgent"]);
        Assert.False(item.Expected.ContainsKey("mood"));
        Assert.Equal("reviewer-ui", item.Source);
        Assert.Equal(1, summary.DecisionsRead);
        Assert.Equal(1, summary.ItemsWritten);
    }

    [Fact]
    public async Task WithoutConfirmedOutcome_IsSkipped()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Signal("d1", At("10:30"), "mailer", "complained", 1, "Wrote back within the hour."));

        Assert.Equal(1, summary.DecisionsRead);
        Assert.Equal(0, summary.ItemsWritten);
        Assert.Equal(1, summary.SkippedWithoutConfirmedOutcome);
        Assert.Empty(dataset);
    }

    [Fact]
    public async Task WithImages_IsSkipped()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1", images: 1),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"queue":"technical"}"""));

        Assert.Equal(1, summary.SkippedWithImages);
        Assert.Equal(0, summary.ItemsWritten);
        Assert.Empty(dataset);
    }

    [Fact]
    public async Task EachUnfitAnswer_IsDropped()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"nope":true,"urgent":"technical","queue":"sales","mood":3}"""));

        Assert.Equal(4, summary.KeysDropped);
        Assert.Equal(1, summary.SkippedWithoutValidKey);
        Assert.Equal(0, summary.ItemsWritten);
        Assert.Empty(dataset);
    }

    [Fact]
    public async Task InvalidLaterAnswer_DoesNotReplaceAValidEarlierOne()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("09:00"), "a", """{"queue":"billing"}"""),
            Confirmed("d1", At("10:00"), "b", """{"queue":"sales"}"""));

        var item = Assert.Single(Load(dataset));
        Assert.Equal(ExpectedAnswer.ForChoice("billing"), item.Expected["queue"]);
        Assert.Equal(1, summary.KeysDropped);
        Assert.Equal(0, summary.KeysOverridden);
    }

    [Fact]
    public async Task LatestAt_WinsPerKey_AndKeysCombine()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("10:00"), "a", """{"queue":"billing","urgent":true}"""),
            Confirmed("d1", At("09:00"), "b", """{"queue":"technical","mood":2}"""));

        var item = Assert.Single(Load(dataset));
        Assert.Equal(ExpectedAnswer.ForChoice("billing"), item.Expected["queue"]);
        Assert.Equal(ExpectedAnswer.ForNoul(true), item.Expected["urgent"]);
        Assert.Equal(ExpectedAnswer.ForScore(2), item.Expected["mood"]);
        Assert.Equal("a, b", item.Source);
        Assert.Equal(1, summary.KeysOverridden);
    }

    [Fact]
    public async Task EqualAt_FallsBackToFileOrder()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("09:00"), "a", """{"queue":"billing"}"""),
            Confirmed("d1", At("09:00"), "b", """{"queue":"technical"}"""));

        var item = Assert.Single(Load(dataset));
        Assert.Equal(ExpectedAnswer.ForChoice("technical"), item.Expected["queue"]);
        Assert.Equal(1, summary.KeysOverridden);
    }

    [Fact]
    public async Task RepeatingTheSameAnswer_IsNotAnOverride()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d1"),
            Confirmed("d1", At("09:00"), "a", """{"queue":"billing"}"""),
            Confirmed("d1", At("10:00"), "b", """{"queue":"billing"}"""));

        var item = Assert.Single(Load(dataset));
        Assert.Equal(ExpectedAnswer.ForChoice("billing"), item.Expected["queue"]);
        Assert.Equal(0, summary.KeysOverridden);
        Assert.Equal("b", item.Source);
    }

    [Fact]
    public async Task FailedRefusedAndCachedDecisions_AreExported()
    {
        var refused = Record("d2", errorKind: "Refused", refusedKeys: ["queue"]);
        var (summary, dataset, _) = await ExportAsync(
            Record("d1", errorKind: "RateLimited"),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"queue":"billing"}"""),
            refused,
            Confirmed("d2", At("09:35"), "reviewer-ui", """{"queue":"technical"}"""),
            Record("d3", cached: true),
            Confirmed("d3", At("09:35"), "reviewer-ui", """{"urgent":false}"""));

        Assert.Equal(3, summary.ItemsWritten);
        Assert.Equal(["d1", "d2", "d3"], Load(dataset).Select(i => i.Id));
    }

    [Fact]
    public async Task StructuredStateAndJsonCriteria_RoundTrip()
    {
        // The emoji written the way the recorder's writer escapes it, so the record line is what a recorder writes.
        const string state = """{"subject":"\uD83D\uDE00 ü","body":{"lines":["Where is my refund?"],"count":1}}""";
        const string criterion = """{"description":"Payments","weight":2}""";
        const string questions =
            """{"queue":{"type":"choice","instructions":"Which queue?","criteria":{"billing":{"json":""" + criterion + """},"technical":"Bugs"}}}""";

        var (_, dataset, _) = await ExportAsync(
            Record("d1", questions: questions, state: state),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"queue":"billing"}"""));

        var item = Assert.Single(Load(dataset));
        Assert.True(item.State.TryGetJson(out var loadedState));
        using (var expected = JsonDocument.Parse(state))
        {
            Assert.Equal(expected.RootElement.GetRawText(), loadedState.GetRawText());
        }

        Assert.Equal("😀 ü", loadedState.GetProperty("subject").GetString());

        var billing = item.Definition.Questions[0].Options[0].Criterion!;
        Assert.True(billing.JsonContent!.Value.TryGetJson(out var loadedCriterion));
        using (var expected = JsonDocument.Parse(criterion))
        {
            Assert.Equal(expected.RootElement.GetRawText(), loadedCriterion.GetRawText());
        }
    }

    [Fact]
    public async Task ItemTheLoaderRejects_IsSkippedNotWritten()
    {
        const string questions = """{"urgent":{"type":"noul","instructions":" "}}""";

        var (summary, dataset, _) = await ExportAsync(
            Record("d1", questions: questions),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"urgent":true}"""));

        Assert.Equal(1, summary.DecisionsRead);
        Assert.Equal(1, summary.SkippedInvalidItem);
        Assert.Equal(0, summary.ItemsWritten);
        Assert.Empty(dataset);
    }

    [Fact]
    public async Task VersionTwoRecord_ExportsLikeVersionOne()
    {
        var confirmed = Confirmed("d1", At("09:35"), "reviewer-ui", """{"queue":"technical","mood":1}""");
        var versionOne = Record("d1");
        var versionTwo = versionOne
            .Replace("\"version\":1", "\"version\":2", StringComparison.Ordinal)
            .Replace("\"kind\":\"noul\",", "\"kind\":\"noul\",\"probabilitySource\":\"verbalized\",", StringComparison.Ordinal);
        Assert.NotEqual(versionOne, versionTwo);

        var (one, datasetOne, _) = await ExportAsync(versionOne, confirmed);
        var (two, datasetTwo, _) = await ExportAsync(versionTwo, confirmed);

        Assert.Equal(1, one.ItemsWritten);
        Assert.Equal(1, two.ItemsWritten);
        Assert.Equal(datasetOne, datasetTwo);
    }

    [Fact]
    public async Task ItemsFollowTheReadersOrder()
    {
        var (summary, dataset, _) = await ExportAsync(
            Record("d2"),
            Record("d1"),
            Record("d3"),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"urgent":true}"""),
            Confirmed("d3", At("09:35"), "reviewer-ui", """{"urgent":true}"""),
            Confirmed("d2", At("09:35"), "reviewer-ui", """{"urgent":true}"""));

        Assert.Equal(3, summary.ItemsWritten);
        Assert.Equal(["d2", "d1", "d3"], Load(dataset).Select(i => i.Id));
    }

    [Fact]
    public async Task SummaryAndItsText_HoldNoIdOrState()
    {
        var (summary, _, _) = await ExportAsync(
            Record("SENTINEL-ID", state: "\"SENTINEL-STATE\""),
            Confirmed("SENTINEL-ID", At("09:35"), "reviewer-ui", """{"queue":"technical","nope":true}"""),
            Record("SENTINEL-ID-2", state: "\"SENTINEL-STATE\""));

        var text = summary.ToString();
        Assert.Contains("items written: 1", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-ID", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL-STATE", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignalsOfExportedItems_AreWrittenInOrder()
    {
        var (summary, _, signals) = await ExportAsync(
            Record("d1"),
            Signal("d1", At("10:30"), "mailer", "complained", 1, "Wrote back within the hour."),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"urgent":true}"""),
            Signal("d1", At("11:00"), "mailer", "reopened"),
            Record("d2"),
            Confirmed("d2", At("09:35"), "reviewer-ui", """{"urgent":false}"""),
            Signal("d2", At("12:00"), "mailer", "praised"));

        var lines = signals.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.Equal("""{"id":"d1","source":"mailer","at":"2026-10-22T10:30:00+00:00","name":"complained","value":1,"note":"Wrote back within the hour.","version":1}""", lines[0]);
        Assert.Equal("""{"id":"d1","source":"mailer","at":"2026-10-22T11:00:00+00:00","name":"reopened","version":1}""", lines[1]);
        Assert.Equal("""{"id":"d2","source":"mailer","at":"2026-10-22T12:00:00+00:00","name":"praised","version":1}""", lines[2]);
        Assert.Equal(3, summary.SignalsWritten);
        Assert.Equal(0, summary.SignalsSkipped);
        Assert.EndsWith("\n", signals, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SignalsOfSkippedDecisions_AreCountedNotWritten()
    {
        var (summary, _, signals) = await ExportAsync(
            Record("d1"),
            Signal("d1", At("10:30"), "mailer", "complained", 1),
            Signal("d1", At("11:00"), "mailer", "reopened"));

        Assert.Empty(signals);
        Assert.Equal(0, summary.SignalsWritten);
        Assert.Equal(2, summary.SignalsSkipped);
    }

    [Fact]
    public async Task NullSignalsStream_CountsWithoutWriting()
    {
        var decisions = await ReadAsync(
            Record("d1"),
            Confirmed("d1", At("09:35"), "reviewer-ui", """{"urgent":true}"""),
            Signal("d1", At("10:30"), "mailer", "complained"));
        using var dataset = new MemoryStream();

        var summary = await DecisionDatasetExport.WriteAsync(Yield(decisions), dataset, null, CancellationToken.None);

        Assert.Equal(1, summary.SignalsWritten);
        Assert.Equal(1, summary.ItemsWritten);
    }

    [Fact]
    public async Task Arguments_AreChecked()
    {
        using var dataset = new MemoryStream();
        await Assert.ThrowsAsync<ArgumentNullException>(() => DecisionDatasetExport.WriteAsync(null!, dataset, null, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => DecisionDatasetExport.WriteAsync(Empty(), null!, null, CancellationToken.None));

        var decisions = await ReadAsync(Record("d1"), Confirmed("d1", At("09:35"), "reviewer-ui", """{"urgent":true}"""));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DecisionDatasetExport.WriteAsync(Yield(decisions), dataset, null, cancelled.Token));
        Assert.Equal(0, dataset.Length);
    }

    // Yields the decisions without looking at the token, so the export's own cancellation check is what throws.
    private static async IAsyncEnumerable<RecordedDecision> Yield(List<RecordedDecision> decisions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        foreach (var decision in decisions)
        {
            await Task.Yield();
            yield return decision;
        }
    }

    private static async IAsyncEnumerable<RecordedDecision> Empty()
    {
        await Task.CompletedTask;
        yield break;
    }
}
