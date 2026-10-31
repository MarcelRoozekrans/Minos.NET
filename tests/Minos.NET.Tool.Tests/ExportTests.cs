using static Minos.Tool.Tests.Harness;

namespace Minos.Tool.Tests;

[Collection(ProcessEnvironment.Name)]
public sealed class ExportTests : IDisposable
{
    // The line shapes of docs/recording.md's "The JSONL format", as the evaluation tests' RecordLines builds them.
    private const string Questions =
        "{\"urgent\":{\"type\":\"noul\",\"instructions\":\"Does this convey urgency?\"}}";

    private const string Answers =
        "[{\"key\":\"urgent\",\"kind\":\"noul\",\"probability\":0.9,\"confidence\":0.8}]";

    private readonly Harness _h = new();

    public void Dispose() => _h.Dispose();

    private static string Record(string id, string state = "\"The app crashes.\"")
        => "{\"kind\":\"record\",\"version\":1,\"decisionId\":\"" + id + "\",\"started\":\"2026-10-22T09:30:00+00:00\",\"latencyMs\":212.5,\"provider\":\"typesafe\","
            + "\"endpoint\":\"https://api.example.com/\",\"defaultModel\":\"jev-latest\",\"cached\":false,\"questions\":" + Questions
            + ",\"state\":" + state + ",\"imageCount\":0,\"answers\":" + Answers + "}";

    private static string Confirmed(string id)
        => "{\"kind\":\"outcome\",\"version\":1,\"decisionId\":\"" + id + "\",\"type\":\"confirmed\",\"source\":\"agent\",\"at\":\"2026-10-23T09:30:00+00:00\",\"confirmed\":{\"urgent\":true}}";

    private static string Signal(string id)
        => "{\"kind\":\"outcome\",\"version\":1,\"decisionId\":\"" + id + "\",\"type\":\"signal\",\"source\":\"crm\",\"at\":\"2026-10-24T09:30:00+00:00\",\"name\":\"escalated\",\"value\":1}";

    private string Records(params string[] lines) => _h.Write("decisions.jsonl", string.Join('\n', lines) + "\n");

    private string DatasetFile => Path.Combine(_h.Out, "dataset.jsonl");

    private string SignalsFile => Path.Combine(_h.Out, "dataset.signals.jsonl");

    [Fact]
    public async Task Export_WritesBothFiles_AndExitsZero()
    {
        var records = Records(Record("a"), Confirmed("a"), Record("b"), Confirmed("b"), Signal("b"));

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.True(File.Exists(DatasetFile));
        Assert.True(File.Exists(SignalsFile));
        Assert.Contains(DatasetFile, run.Stdout, StringComparison.Ordinal);
        Assert.Contains(SignalsFile, run.Stdout, StringComparison.Ordinal);
        Assert.Contains("items written: 2", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportThenCalibrate_Runs()
    {
        var records = Records(Record("a"), Confirmed("a"), Record("b"), Confirmed("b"));
        var exported = await Invoke(null, "export", "--records", records, "--out", _h.Out);
        Assert.Equal(0, exported.Exit);

        var run = await Invoke(new FakeSystemOne(), "calibrate", "--dataset", DatasetFile, "--config", _h.Config(), "--out", Path.Combine(_h.Dir, "cal"));

        Assert.Equal(0, run.Exit);
    }

    [Fact]
    public async Task Export_CutLastLine_ExportsTheRestAndExitsZero()
    {
        var records = _h.Write("cut.jsonl", Record("a") + "\n" + Confirmed("a") + "\n" + "{\"kind\":\"outcome\",\"version\":1,\"deci");

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.Contains("items written: 1", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("line 3", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_NothingExportable_ExitsTwo_AndLeavesNoFiles()
    {
        var records = Records(Record("a"), Record("b"));

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.False(File.Exists(DatasetFile));
        Assert.False(File.Exists(SignalsFile));
    }

    [Fact]
    public async Task Export_MissingFile_ExitsTwo()
    {
        var run = await Invoke(null, "export", "--records", Path.Combine(_h.Dir, "nope.jsonl"), "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.Contains("does not exist", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_UnwritableOut_ExitsOne()
    {
        var records = Records(Record("a"), Confirmed("a"));
        var blocker = _h.Write("blocker", "a file");

        var run = await Invoke(null, "export", "--records", records, "--out", blocker);

        Assert.Equal(1, run.Exit);
    }

    [Fact]
    public async Task Export_Cancelled_Exits130_AndLeavesNoFiles()
    {
        // The cut last line is a problem, which export prints after it has read the records: the cancellation comes when
        // writing is about to start, so the files are created and then deleted.
        var records = _h.Write("cut.jsonl", Record("a") + "\n" + Confirmed("a") + "\n{\"kind\":\"outcome\",\"version\":1,\"deci");

        var run = await InvokeCancellingOnFirstProblem("export", "--records", records, "--out", _h.Out);

        Assert.Equal(130, run.Exit);
        Assert.True(Directory.Exists(_h.Out));
        Assert.False(File.Exists(DatasetFile));
        Assert.False(File.Exists(SignalsFile));
    }

    [Fact]
    public async Task Export_OverwritesAnEarlierExport()
    {
        var first = Records(Record("a"), Confirmed("a"), Signal("a"), Record("b"), Confirmed("b"));
        Assert.Equal(0, (await Invoke(null, "export", "--records", first, "--out", _h.Out)).Exit);
        Assert.NotEmpty(File.ReadAllText(SignalsFile));
        var second = _h.Write("second.jsonl", Record("c") + "\n" + Confirmed("c") + "\n");

        var run = await Invoke(null, "export", "--records", second, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        var lines = File.ReadAllLines(DatasetFile);
        Assert.Single(lines);
        Assert.Contains("\"c\"", lines[0], StringComparison.Ordinal);
        Assert.Empty(File.ReadAllText(SignalsFile));
    }

    [Fact]
    public async Task Export_OutputHoldsNoIdOrState()
    {
        // An outcome with no record before it, and a cut last line, are reported as problems.
        var records = _h.Write(
            "sentinel.jsonl",
            string.Join('\n', (string[])[Record("SENTINEL-ID", "\"SENTINEL-STATE\""), Confirmed("SENTINEL-ID"), Signal("SENTINEL-ID"), Confirmed("SENTINEL-ORPHAN")])
                + "\n{\"kind\":\"record\",\"version\":1,\"decisionId\":\"SENTINEL-CUT\",\"state\":\"SENTINEL");

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.Contains("line 4", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("line 5", run.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", run.Stdout, StringComparison.Ordinal);
        Assert.DoesNotContain("SENTINEL", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_EveryLineAProblem_ExitsTwo()
    {
        var records = Records("not json", "{\"kind\":\"unknown\",\"version\":1}");

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(2, run.Exit);
        Assert.Contains("line 1", run.Stderr, StringComparison.Ordinal);
        Assert.Contains("line 2", run.Stderr, StringComparison.Ordinal);
        Assert.False(File.Exists(DatasetFile));
    }

    [Fact]
    public async Task Export_FailsAfterTheDatasetIsCreated_ExitsOne_AndDeletesIt()
    {
        var records = Records(Record("a"), Confirmed("a"));

        // The signals path is a directory, so the dataset file is created and opening the signals file then throws.
        Directory.CreateDirectory(SignalsFile);
        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(1, run.Exit);
        Assert.False(File.Exists(DatasetFile));
        Assert.Contains("The output cannot be written", run.Stderr, StringComparison.Ordinal);
    }

    [Fact]
    public void DeletingTheOutput_IsBestEffort()
    {
        // A cleanup that fails must not replace the exception that started it, such as a cancellation.
        var dataset = _h.Write("dataset.jsonl", "x");
        var signals = Directory.CreateDirectory(Path.Combine(_h.Dir, "signals-dir")).FullName;

        CommandSupport.DeleteBoth(dataset, signals);

        Assert.False(File.Exists(dataset));
        Assert.True(Directory.Exists(signals));
    }

    [Fact]
    public async Task Export_MakesFilesOnlyTheirOwnerCanRead()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var records = Records(Record("a"), Confirmed("a"));

        var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

        Assert.Equal(0, run.Exit);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(DatasetFile));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(SignalsFile));
    }

    [Fact]
    public async Task Export_UnreadableRecords_IsAnInputProblem_NotAnOutputProblem()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var records = Records(Record("a"), Confirmed("a"));
        File.SetUnixFileMode(records, UnixFileMode.None);
        try
        {
            // Running as root ignores the mode, so the file is readable and there is nothing to check.
            try
            {
                File.ReadAllBytes(records);
                return;
            }
            catch (UnauthorizedAccessException)
            {
            }

            var run = await Invoke(null, "export", "--records", records, "--out", _h.Out);

            Assert.Equal(ExitCodes.Usage, run.Exit);
            Assert.Contains("The input cannot be read:", run.Stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("output cannot be written", run.Stderr, StringComparison.Ordinal);
            Assert.False(Directory.Exists(_h.Out));
        }
        finally
        {
            File.SetUnixFileMode(records, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
