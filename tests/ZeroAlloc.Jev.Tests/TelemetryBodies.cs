using System.Buffers;
using System.Text;
using ZeroAlloc.Jev.Transport;

namespace ZeroAlloc.Jev.Tests;

/// <summary>Pooled bodies and answer literals for the telemetry tests.</summary>
internal static class TelemetryBodies
{
    /// <summary>A Choice answer whose <c>confidence</c> is 0.81.</summary>
    public const string ChoiceJson = """{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81}""";

    /// <summary>A Score answer whose <c>confidence</c> is 0.92.</summary>
    public const string ScoreJson = """{"type":"score","score":1.05,"legend":{"0":"Calm","1":"Frustrated"},"probabilities":{"0":0.05,"1":0.95},"confidence":0.92}""";

    /// <summary>A Noul answer, which has no <c>confidence</c>.</summary>
    public const string NoulJson = """{"type":"noul","noul":0.95}""";

    /// <summary>Copies <paramref name="json"/> into a pooled body, which the caller owns and disposes.</summary>
    public static RawJson RawJsonOf(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        var raw = RawJson.Create(ArrayPool<byte>.Shared, bytes.Length);
        bytes.CopyTo(raw.GetSpan(bytes.Length));
        raw.Advance(bytes.Length);
        return raw;
    }

    /// <summary>A request body, which the typed operations own and dispose.</summary>
    public static RawJson EmptyBody() => RawJsonOf("{}");
}
