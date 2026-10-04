using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>One harness run in the phase's shared result format, written as <c>dotnet-&lt;machine&gt;.json</c>.</summary>
/// <param name="Machine">The machine the run measured.</param>
/// <param name="Results">One entry per client.</param>
public sealed record ResultFile(MachineInfo Machine, IReadOnlyList<ClientResult> Results)
{
    /// <summary>Serializes the file in the shared format: camelCase names, indented.</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson() => JsonSerializer.Serialize(this, CompareJsonContext.Default.ResultFile);
}

/// <summary>The machine a run measured.</summary>
/// <param name="Name">The machine's name.</param>
/// <param name="Os">The operating system.</param>
/// <param name="Cpu">The processor.</param>
/// <param name="Date">When the run finished, as an ISO 8601 UTC timestamp such as <c>2026-10-04T10:05:13Z</c>.</param>
/// <param name="MockCeilingPerSecond">
/// The mock's own ceiling: completed calls per second of the raw client at 64 workers, so each client's throughput can be
/// read against it.
/// </param>
public sealed record MachineInfo(string Name, string Os, string Cpu, string Date, double MockCeilingPerSecond);

/// <summary>One client's figures.</summary>
/// <param name="Client">The client's name.</param>
/// <param name="Library">The library the client is built on.</param>
/// <param name="Version">The library's version.</param>
/// <param name="Runtime">The runtime, <c>.NET</c>.</param>
/// <param name="RuntimeVersion">The runtime's version.</param>
/// <param name="LatencyMs">One call at a time: mean, median and 99th percentile, in milliseconds.</param>
/// <param name="ThroughputPerSecond">Completed calls per second at <paramref name="Concurrency"/>.</param>
/// <param name="Concurrency">How many workers called at once in the throughput run.</param>
/// <param name="AllocatedBytesPerCall">Bytes allocated per call, from BenchmarkDotNet's memory diagnoser.</param>
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
[JsonSerializable(typeof(JsonElement))]
internal sealed partial class CompareJsonContext : JsonSerializerContext;
