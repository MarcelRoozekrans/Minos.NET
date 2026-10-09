using System.Text;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

namespace Minos.Benchmarks;

public sealed record BenchState(string Message, string Channel);

[JsonSerializable(typeof(BenchState))]
internal sealed partial class BenchJsonContext : JsonSerializerContext;

/// <summary>The JevContent factories callers use for state, instructions and criteria.</summary>
[MemoryDiagnoser]
public class ContentBenchmarks
{
    private readonly BenchState _state = new("Please send me your password", "email");
    private readonly byte[] _utf8 = Encoding.UTF8.GetBytes("""{"message":"Please send me your password","channel":"email"}""");

    [Benchmark]
    public JevContent FromValue() => JevContent.FromValue(_state, BenchJsonContext.Default.BenchState);

    [Benchmark]
    public JevContent FromUtf8Json() => JevContent.FromUtf8Json(_utf8);
}
