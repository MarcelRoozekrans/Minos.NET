using Minos.Conformance;
using Minos.Conformance.Tests;
using Minos.SystemOne;
using Xunit.Abstractions;

namespace Minos.Live.Tests;

/// <summary>
/// Records the conformance fixtures for TypeSafe, OpenRouter and the chat bridge over OpenRouter: every probe against the
/// real endpoint, billed, with no retries. Each run writes <c>&lt;target&gt;.json</c> and <c>&lt;target&gt;.report.md</c>
/// to <c>MINOS_CONFORMANCE_OUT</c>, which the Live smoke workflow uploads as an artifact; the fixtures reach the
/// repository through a pull request, never from CI. A check that fails fails the test, after the files are written.
/// </summary>
public sealed class ConformanceRecordTests
{
    private const string OutputVariable = "MINOS_CONFORMANCE_OUT";

    private readonly ITestOutputHelper _output;

    public ConformanceRecordTests(ITestOutputHelper output) => _output = output;

    [LiveFact(SystemOneProvider.TypeSafe)]
    public Task TypeSafe_RecordsItsFixture()
        => RecordAsync(SystemOneTargets.TypeSafe(model: Live.Model(SystemOneProvider.TypeSafe), maxRetries: 0));

    [LiveFact(SystemOneProvider.OpenRouter)]
    public Task OpenRouter_RecordsItsFixture()
        => RecordAsync(SystemOneTargets.OpenRouter(model: Live.Model(SystemOneProvider.OpenRouter), maxRetries: 0));

    // The bridge over OpenRouter's chat completions, reading logprobs; MINOS_LIVE_CHAT_MODEL picks a model that returns them.
    [LiveFact(SystemOneProvider.OpenRouter)]
    public Task ChatOpenRouter_RecordsItsFixture()
        => RecordAsync(ChatTargets.OpenRouter(
            Environment.GetEnvironmentVariable("MINOS_LIVE_CHAT_MODEL") is { Length: > 0 } model ? model : ChatTargets.DefaultModel,
            maxRetries: 0));

    private async Task RecordAsync(ConformanceTarget target)
    {
        var directory = Environment.GetEnvironmentVariable(OutputVariable) is { Length: > 0 } configured
            ? configured
            : Path.Combine(AppContext.BaseDirectory, "conformance");
        Directory.CreateDirectory(directory);
        var fixturePath = Path.Combine(directory, target.Name + ".json");

        var report = await new ConformanceRunner().RunAsync(target, ConformanceMode.Record, fixturePath);

        var markdown = report.ToMarkdown();
        await File.WriteAllTextAsync(Path.Combine(directory, target.Name + ".report.md"), markdown);
        _output.WriteLine(markdown);
        Assert.Null(ConformanceFixture.Load(fixturePath).HeaderProblem());
        Assert.Empty(report.FailedChecks);
    }
}
