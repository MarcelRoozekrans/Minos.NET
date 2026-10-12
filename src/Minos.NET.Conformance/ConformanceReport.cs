using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minos.Conformance;

/// <summary>A run's result: the target, the model, the mode, the fixture's source and date, and every check's result. Written as JSON and as markdown.</summary>
public sealed class ConformanceReport
{
    /// <summary>The probe name the run-level checks are listed under.</summary>
    public const string RunScope = "run";

    /// <summary>Gets the target's name.</summary>
    public required string Target { get; init; }

    /// <summary>Gets the target's wire format.</summary>
    public required ConformanceWire Wire { get; init; }

    /// <summary>Gets the model the probes asked for.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the target's model rule, as R2 applies it, or <see langword="null"/> when it states none.</summary>
    public string? ModelRule { get; init; }

    /// <summary>Gets how the run was made.</summary>
    public required ConformanceMode Mode { get; init; }

    /// <summary>Gets the replayed fixture's source, or <see langword="null"/> for a live or recording run.</summary>
    public string? FixtureSource { get; init; }

    /// <summary>Gets the replayed fixture's recording date, or <see langword="null"/>.</summary>
    public string? FixtureRecorded { get; init; }

    /// <summary>Gets each probe's results, in probe order.</summary>
    public required IReadOnlyList<ProbeReport> Probes { get; init; }

    /// <summary>Gets the results of the checks that read the whole run: E4.</summary>
    public required IReadOnlyList<CheckResult> RunChecks { get; init; }

    /// <summary>Gets how many checks passed.</summary>
    [JsonIgnore]
    public int Passed => Count(CheckOutcome.Pass);

    /// <summary>Gets how many checks failed.</summary>
    [JsonIgnore]
    public int Failed => Count(CheckOutcome.Fail);

    /// <summary>Gets how many checks were skipped.</summary>
    [JsonIgnore]
    public int Skipped => Count(CheckOutcome.Skipped);

    /// <summary>Gets the failed checks, each as <c>probe:check</c>, in report order.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> FailedChecks => Named(CheckOutcome.Fail);

    /// <summary>Gets the skipped checks, each as <c>probe:check</c>, in report order; E4 is <c>run:E4</c>.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> SkippedChecks => Named(CheckOutcome.Skipped);

    /// <summary>Reads a report from its JSON.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The report.</returns>
    /// <exception cref="InvalidDataException">The JSON is <c>null</c>.</exception>
    public static ConformanceReport FromJson(string json)
        => JsonSerializer.Deserialize(json, ConformanceJson.Readable.ConformanceReport)
            ?? throw new InvalidDataException("The report is empty.");

    /// <summary>Writes the report as indented JSON with a final newline.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, ConformanceJson.Readable.ConformanceReport) + "\n";

    /// <summary>Writes the report as markdown: a summary list and one table row per check.</summary>
    /// <returns>The markdown.</returns>
    public string ToMarkdown()
    {
        var text = new StringBuilder();
        text.Append("# Conformance: ").Append(Target).Append("\n\n");
        text.Append("- Mode: ").Append(Mode.ToString()).Append('\n');
        text.Append("- Wire: ").Append(Wire.ToString()).Append('\n');
        text.Append("- Model: `").Append(Model).Append("`\n");
        text.Append("- Model rule: ").Append(ModelRule ?? "none stated").Append('\n');
        text.Append("- Fixture: ").Append(FixtureSource ?? "none");
        if (FixtureRecorded is { } recorded)
        {
            text.Append(", recorded ").Append(recorded);
        }

        text.Append('\n');
        text.Append(CultureInfo.InvariantCulture, $"- Result: {Passed} passed, {Failed} failed, {Skipped} skipped").Append("\n\n");
        text.Append("| Probe | Check | Outcome | Detail |\n");
        text.Append("| --- | --- | --- | --- |\n");
        foreach (var (scope, check) in AllChecks())
        {
            Row(text, scope, check);
        }

        return text.ToString();
    }

    private static void Row(StringBuilder text, string probe, CheckResult check)
        => text.Append("| ").Append(probe)
            .Append(" | ").Append(check.Id)
            .Append(" | ").Append(OutcomeText(check.Outcome))
            .Append(" | ").Append(check.Detail.Replace("|", "\\|", StringComparison.Ordinal))
            .Append(" |\n");

    private static string OutcomeText(CheckOutcome outcome) => outcome switch
    {
        CheckOutcome.Pass => "pass",
        CheckOutcome.Fail => "fail",
        _ => "skipped",
    };

    private IEnumerable<(string Scope, CheckResult Check)> AllChecks()
    {
        foreach (var probe in Probes)
        {
            foreach (var check in probe.Checks)
            {
                yield return (probe.Probe, check);
            }
        }

        foreach (var check in RunChecks)
        {
            yield return (RunScope, check);
        }
    }

    private int Count(CheckOutcome outcome)
    {
        var count = 0;
        foreach (var (_, check) in AllChecks())
        {
            count += check.Outcome == outcome ? 1 : 0;
        }

        return count;
    }

    private List<string> Named(CheckOutcome outcome)
    {
        var named = new List<string>();
        foreach (var (scope, check) in AllChecks())
        {
            if (check.Outcome == outcome)
            {
                named.Add(scope + ":" + check.Id);
            }
        }

        return named;
    }
}
