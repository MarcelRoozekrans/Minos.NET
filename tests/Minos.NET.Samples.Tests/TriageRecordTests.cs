using System.Globalization;
using Minos.Samples.TicketTriage;

namespace Minos.Samples.Tests;

public sealed class TriageRecordTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), "triage-" + Guid.NewGuid().ToString("N") + ".jsonl");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    private static TriageRecord Full() => new(
        "t-1",
        12.5,
        null,
        [
            new TriageQuestion(
                "is_urgent",
                new TriageAnswer("noul", 0.12, null, null),
                "primary",
                "Confident",
                0.88,
                null,
                new TriageAnswer("noul", 0.12, null, null)),
            new TriageQuestion(
                "category",
                new TriageAnswer("choice", null, "card_arrival", 0.4),
                "target",
                "Escalated",
                0.4,
                0.9,
                new TriageAnswer("choice", null, "card_arrival", 0.9)),
        ],
        [
            new TriageCall("local", null, null, null, null, "Answered", 1.5),
            new TriageCall("remote", "model-x", 100, 20, 0.0003, "Failed", null),
        ]);

    [Fact]
    public void Line_ThenRead_RoundTripsARecordWithAllMembers()
    {
        var record = Full();
        File.WriteAllText(_path, TriageRecordWriter.Line(record) + "\n");

        var read = TriageRecordReader.Read(_path);

        var back = Assert.Single(read);
        Assert.Equal(record.Id, back.Id);
        Assert.Equal(record.TotalMs, back.TotalMs);
        Assert.Null(back.Error);
        Assert.Equal(record.Questions, back.Questions);
        Assert.Equal(record.Calls, back.Calls);
    }

    [Fact]
    public void Line_OfAFailedItem_HasOnlyIdTotalAndError()
    {
        var line = TriageRecordWriter.Line(new TriageRecord("t-9", 3, "TimeoutException", [], []));

        Assert.Equal("{\"v\":1,\"id\":\"t-9\",\"totalMs\":3,\"error\":\"TimeoutException\",\"questions\":[],\"calls\":[]}", line);
        var back = Assert.Single(ReadLines(line));
        Assert.Equal("TimeoutException", back.Error);
        Assert.Empty(back.Questions);
        Assert.Empty(back.Calls);
    }

    [Fact]
    public void Line_IsOneLineWithNoNewline_AndInvariantCulture()
    {
        var record = new TriageRecord(
            "t-2",
            0.5,
            null,
            [new TriageQuestion("k", new TriageAnswer("noul", 0.1234567, null, null), "primary", "Confident", 0.5, null, new TriageAnswer("noul", 0.1234567, null, null))],
            []);
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("nl-NL");
        try
        {
            var line = TriageRecordWriter.Line(record);

            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\r', line);
            Assert.Equal(
                "{\"v\":1,\"id\":\"t-2\",\"totalMs\":0.5,\"questions\":[{\"key\":\"k\",\"local\":{\"kind\":\"noul\",\"p\":0.1234567},\"answeredBy\":\"primary\",\"reason\":\"Confident\",\"primaryCertainty\":0.5,\"final\":{\"kind\":\"noul\",\"p\":0.1234567}}],\"calls\":[]}",
                line);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("score")]
    [InlineData("reason")]
    [InlineData("version")]
    public void Read_RejectsAScoreAnswer_AFellBackReason_AndVersion2(string defect)
    {
        var good = TriageRecordWriter.Line(Full());
        var bad = defect switch
        {
            "score" => good.Replace("\"kind\":\"noul\"", "\"kind\":\"score\"", StringComparison.Ordinal),
            "reason" => good.Replace("\"Confident\"", "\"FellBack\"", StringComparison.Ordinal),
            _ => good.Replace("\"v\":1", "\"v\":2", StringComparison.Ordinal),
        };
        Assert.NotEqual(good, bad);
        File.WriteAllText(_path, good + "\n" + bad + "\n");

        var ex = Assert.Throws<InvalidDataException>(() => TriageRecordReader.Read(_path));

        Assert.Contains("line 2", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("t-1", ex.Message, StringComparison.Ordinal);
    }

    private IReadOnlyList<TriageRecord> ReadLines(string line)
    {
        File.WriteAllText(_path, line + "\n");
        return TriageRecordReader.Read(_path);
    }
}
