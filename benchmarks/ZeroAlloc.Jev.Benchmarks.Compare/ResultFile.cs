using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>One harness run in the phase's shared result format, written as <c>dotnet-&lt;machine&gt;.json</c>.</summary>
/// <param name="Machine">The machine the run measured.</param>
/// <param name="Results">One entry per client.</param>
public sealed record ResultFile(MachineInfo Machine, IReadOnlyList<ClientResult> Results)
{
    /// <summary>
    /// Gets the order the clients were measured in, or <see langword="null"/> for a harness with one client; left out
    /// when null.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MeasurementOrder? Order { get; init; }

    /// <summary>Serializes the file in the shared format: camelCase names, indented.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, CompareJsonContext.Default.ResultFile);
}

/// <summary>How the harness ordered its clients.</summary>
/// <param name="LatencyRounds">How many interleaved latency rounds ran.</param>
/// <param name="CallsPerRound">How many timed calls each client made in each round.</param>
/// <param name="Rotation">Where the first round started in the base client order; round <c>r</c> starts one further.</param>
/// <param name="Throughput">The clients in the order their throughput windows ran, one client at a time.</param>
public sealed record MeasurementOrder(int LatencyRounds, int CallsPerRound, int Rotation, IReadOnlyList<string> Throughput);

/// <summary>The machine a run measured.</summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Os">The operating system.</param>
/// <param name="Cpu">The processor.</param>
/// <param name="Date">When the run finished, as an ISO 8601 UTC timestamp such as <c>2026-10-04T10:05:13Z</c>.</param>
/// <param name="MockCeilingPerSecond">
/// The mock ceiling: the best completed calls per second of the raw client at 16, 32 and 64 workers. It is a lower
/// bound on what the mock can serve, since nothing shows whether the mock or the client side saturated, and each
/// client's throughput is read against it.
/// </param>
/// <param name="Cores">
/// The cores the harness ran on, as a list such as <c>10-19</c>, or <see langword="null"/> when it was not pinned. The
/// runner gives the mock the other cores.
/// </param>
/// <param name="MockCores">
/// The cores the runner pinned the mock to, as a list such as <c>12-19</c>, or <see langword="null"/> when it was not
/// pinned.
/// </param>
public sealed record MachineInfo(string Name, string Os, string Cpu, string Date, double MockCeilingPerSecond, string? Cores, string? MockCores);

/// <summary>One client's figures.</summary>
/// <param name="Client">The client's name.</param>
/// <param name="Library">The library the client is built on.</param>
/// <param name="Version">The library's version.</param>
/// <param name="Runtime">The runtime, <c>.NET</c>.</param>
/// <param name="RuntimeVersion">The runtime's version.</param>
/// <param name="LatencyMs">One call at a time: mean, median and 99th percentile, in milliseconds.</param>
/// <param name="ThroughputPerSecond">Completed calls per second at <paramref name="Concurrency"/>.</param>
/// <param name="Concurrency">How many workers called at once in the throughput run.</param>
/// <param name="AllocatedBytesPerCall">
/// Bytes allocated per call, from BenchmarkDotNet's memory diagnoser. The .NET harness always sets it; the format
/// allows null for the JS and Python harnesses only.
/// </param>
public sealed record ClientResult(
    string Client,
    string Library,
    string Version,
    string Runtime,
    string RuntimeVersion,
    LatencyFigures LatencyMs,
    double ThroughputPerSecond,
    int Concurrency,
    long? AllocatedBytesPerCall)
{
    /// <summary>Gets how this client's calls differ from the others', or <see langword="null"/>; left out when null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}

/// <summary>Per-call latency in milliseconds.</summary>
/// <param name="Mean">The mean.</param>
/// <param name="P50">The median.</param>
/// <param name="P99">The 99th percentile.</param>
public sealed record LatencyFigures(double Mean, double P50, double P99);

/// <summary>The harness's source-generated serializer metadata.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ResultFile))]
internal sealed partial class CompareJsonContext : JsonSerializerContext;
