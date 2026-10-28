using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Minos.Conformance;

/// <summary>
/// A target's recorded exchanges, one per probe, and where they came from. On <see cref="ConformanceWire.ChatCompletions"/>
/// a probe holds one exchange per question instead. It never holds a key or any header outside the allowlist, so it can
/// be committed.
/// </summary>
public sealed class ConformanceFixture
{
    /// <summary>The source of a fixture recorded from the live endpoint.</summary>
    public const string RecordedSource = "recorded";

    /// <summary>The source of a fixture built from a provider's documentation.</summary>
    public const string GuideSource = "guide";

    /// <summary>The source of a fixture built from this repository's unit-test fixtures.</summary>
    public const string TestsSource = "tests";

    /// <summary>Gets the target's name.</summary>
    public required string Target { get; init; }

    /// <summary>Gets where the exchanges came from: <see cref="RecordedSource"/>, <see cref="GuideSource"/> or <see cref="TestsSource"/>.</summary>
    public required string Source { get; init; }

    /// <summary>Gets the model the responses named, required for a recorded fixture.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the recording date, as <c>yyyy-MM-dd</c>, required for a recorded fixture.</summary>
    public string? Recorded { get; init; }

    /// <summary>Gets how the fixture was made, for a reader.</summary>
    public string? Note { get; init; }

    /// <summary>Gets one exchange per probe, or on <see cref="ConformanceWire.ChatCompletions"/> one per question.</summary>
    public required IReadOnlyList<ConformanceExchange> Exchanges { get; init; }

    /// <summary>Reads a fixture file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The fixture.</returns>
    public static ConformanceFixture Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads a fixture from its JSON.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The fixture.</returns>
    /// <exception cref="InvalidDataException">The JSON is <c>null</c>.</exception>
    public static ConformanceFixture Parse(string json)
        => JsonSerializer.Deserialize(json, ConformanceJson.Readable.ConformanceFixture)
            ?? throw new InvalidDataException("The fixture is empty.");

    /// <summary>Writes the fixture as indented JSON with a final newline.</summary>
    /// <returns>The JSON.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, ConformanceJson.Readable.ConformanceFixture) + "\n";

    /// <summary>Writes the fixture to a file.</summary>
    /// <param name="path">The file.</param>
    public void Save(string path) => File.WriteAllText(path, ToJson());

    /// <summary>Gets the probe's first exchange, or <see langword="null"/>.</summary>
    /// <param name="probe">The probe's id.</param>
    /// <returns>The exchange.</returns>
    public ConformanceExchange? Find(string probe)
    {
        foreach (var exchange in Exchanges)
        {
            if (string.Equals(exchange.Probe, probe, StringComparison.Ordinal))
            {
                return exchange;
            }
        }

        return null;
    }

    /// <summary>Gets every exchange of a probe, in fixture order; empty when it has none.</summary>
    /// <param name="probe">The probe's id.</param>
    /// <returns>The exchanges.</returns>
    public IReadOnlyList<ConformanceExchange> FindAll(string probe)
    {
        var found = new List<ConformanceExchange>();
        foreach (var exchange in Exchanges)
        {
            if (string.Equals(exchange.Probe, probe, StringComparison.Ordinal))
            {
                found.Add(exchange);
            }
        }

        return found;
    }

    /// <summary>
    /// Says what is wrong with the header, or returns <see langword="null"/>: the source is one of the three, a recorded
    /// fixture names its model and its date, and every date is <c>yyyy-MM-dd</c>.
    /// </summary>
    /// <returns>The problem, or <see langword="null"/>.</returns>
    public string? HeaderProblem()
    {
        if (Source is not (RecordedSource or GuideSource or TestsSource))
        {
            return "The source '" + Source + "' is not recorded, guide or tests.";
        }

        if (string.Equals(Source, RecordedSource, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(Model))
        {
            return "A recorded fixture names its model.";
        }

        if (string.Equals(Source, RecordedSource, StringComparison.Ordinal) && Recorded is null)
        {
            return "A recorded fixture names its date.";
        }

        if (Recorded is not null && !IsDate(Recorded))
        {
            return "The date '" + Recorded + "' is not yyyy-MM-dd.";
        }

        foreach (var exchange in Exchanges)
        {
            if (exchange.Recorded is { } date && !IsDate(date))
            {
                return "The " + exchange.Probe + " exchange's date '" + date + "' is not yyyy-MM-dd.";
            }
        }

        return null;
    }

    private static bool IsDate(string text)
        => DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

/// <summary>The serializer options fixtures and reports share: camelCase, indented with LF, nulls left out, text kept readable.</summary>
internal static class ConformanceJson
{
    /// <summary>
    /// Gets the generated context with an encoder that leaves apostrophes and other text as it was sent. The files are read
    /// by people and by this code and never embedded in HTML.
    /// </summary>
    public static ConformanceJsonContext Readable { get; } = new(new JsonSerializerOptions(ConformanceJsonContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    NewLine = "\n",
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ConformanceFixture))]
[JsonSerializable(typeof(ConformanceReport))]
[JsonSerializable(typeof(string))]
internal sealed partial class ConformanceJsonContext : JsonSerializerContext;
