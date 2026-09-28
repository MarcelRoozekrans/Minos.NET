using System.Net;
using System.Text;
using System.Text.Json;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary>
/// Allocation budgets for the hot paths the smoke app exercises, measured under the published Native AOT binary.
/// Each gate runs <see cref="AllocationGate"/> for 1000 iterations and turns a budget breach into a failed
/// <c>Check</c>, rather than letting <see cref="AllocationGate"/>'s exception crash the whole run.
/// </summary>
internal static class AllocationChecks
{
    private const string NoulAnswerJson = """{"type":"noul","noul":0.95}""";
    private const string ChoiceAnswerJson = """{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7}""";
    private const string ScoreAnswerJson = """{"type":"score","score":1.9,"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}""";
    private const string TriageAnswersJson = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";
    private const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string TriageResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

    /// <summary>The generated <c>SmokeTriage.Parse</c>, over the whole triage answer set.</summary>
    public static void GeneratedParse()
    {
        var answers = Encoding.UTF8.GetBytes(TriageAnswersJson);

        // Measured ~176 B/call on published win-x64 AOT: the SmokeTriage result record plus its shared probability
        // buffer (double[3]), rounded up to the next multiple of 64. This gate allocates only the result record
        // and its arrays, whose sizes are identical on 64-bit Linux and Windows, so the thin margin above the
        // measurement is deliberate, not an oversight.
        Gate(
            budgetBytes: 192,
            action: () =>
            {
                var reader = new Utf8JsonReader(answers);
                reader.Read();
                _ = SmokeTriage.Parse(ref reader);
            },
            label: "GeneratedParse",
            passDescription: "the generated Parse stays within its allocation budget");
    }

    /// <summary><see cref="JevAnswerReader.ReadNoul"/> over a fixed Noul answer.</summary>
    public static void ReadNoul()
    {
        var answer = Encoding.UTF8.GetBytes(NoulAnswerJson);

        // Measured 0 B/call on published win-x64 AOT: ReadNoul only reads a struct off the span.
        Gate(
            budgetBytes: 0,
            action: () =>
            {
                var reader = new Utf8JsonReader(answer);
                reader.Read();
                _ = JevAnswerReader.ReadNoul(ref reader);
            },
            label: "ReadNoul",
            passDescription: "ReadNoul stays within its allocation budget");
    }

    /// <summary><see cref="JevAnswerReader.ReadChoice{T}"/> into a caller-owned buffer.</summary>
    public static void ReadChoice()
    {
        var answer = Encoding.UTF8.GetBytes(ChoiceAnswerJson);
        var buffer = new double[TeamOptions.Instance.Count];

        // Measured 0 B/call on published win-x64 AOT: ReadChoice writes into the caller-owned buffer and returns a struct.
        Gate(
            budgetBytes: 0,
            action: () =>
            {
                var reader = new Utf8JsonReader(answer);
                reader.Read();
                _ = JevAnswerReader.ReadChoice(ref reader, TeamOptions.Instance, buffer, 0);
            },
            label: "ReadChoice",
            passDescription: "ReadChoice stays within its allocation budget");
    }

    /// <summary><see cref="JevAnswerReader.ReadScore{T}"/> into a caller-owned buffer.</summary>
    public static void ReadScore()
    {
        var answer = Encoding.UTF8.GetBytes(ScoreAnswerJson);
        var buffer = new double[UrgencyOptions.Instance.Count];

        // Measured 0 B/call on published win-x64 AOT: ReadScore writes into the caller-owned buffer and returns a struct.
        Gate(
            budgetBytes: 0,
            action: () =>
            {
                var reader = new Utf8JsonReader(answer);
                reader.Read();
                _ = JevAnswerReader.ReadScore(ref reader, UrgencyOptions.Instance, buffer, 0);
            },
            label: "ReadScore",
            passDescription: "ReadScore stays within its allocation budget");
    }

    /// <summary><see cref="JevClient.EvaluateAsync"/> over a canned handler: request serialization and response
    /// deserialization through the public API only, since <c>JevJsonContext</c> is internal.</summary>
    public static void EvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });
        var request = new SystemOneRequest
        {
            State = "Help! My payouts have been failing for 3 days.",
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
            },
        };

        // Measured ~4592 B/call on published win-x64 AOT: HttpRequestMessage, headers and content for the request,
        // plus the response's HttpResponseMessage, its body buffering and JSON deserialization into
        // SystemOneResponse. A linux-x64 measurement (dotnet/sdk:10.0 container, 2026-09-27) matches exactly:
        // 4592 B/call. The budget keeps about 10% headroom (5120 B) over that measurement, since HttpClient's
        // internal buffering can still differ across runtime patch versions on either platform.
        GateValueTask(
            budgetBytes: 5120,
            action: () => client.EvaluateAsync(request),
            label: "EvaluateRoundTrip",
            passDescription: "EvaluateAsync stays within its allocation budget");
    }

    /// <summary><see cref="JevClient.EvaluateAsync{T}(string)"/> over a canned handler: the raw, pooled-buffer
    /// path, through the public API only, since <c>TypedEvaluation</c> and <c>RawJson</c> are internal.</summary>
    public static void TypedEvaluateRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });

        // Measured ~3784 B/call on published win-x64 AOT: the request's Utf8JsonWriter and RawJson, plus
        // ZeroAlloc.Rest's own per-attempt allocations (HttpRequestMessage, headers, the MemoryStream the body is
        // copied into, and StreamContent), plus the response's HttpResponseMessage and body buffering, and the
        // async state machines. There is no SystemOneRequest, SystemOneResponse, JevAnswer or questions dictionary
        // on this path, which is why it comes in well below EvaluateRoundTrip's 4592 B measurement. A linux-x64
        // measurement (dotnet/sdk:10.0 container, 2026-09-27) matches exactly: 3784 B/call. The budget keeps about
        // 10% headroom (4224 B, rounded to the next 64 B) over that measurement, for the same cross-platform,
        // cross-patch-version reason as EvaluateRoundTrip's gate. Re-measured after the typed-state path
        // (EvaluateAsync&lt;T, TState&gt;) stopped copying the JSON: still 3784 B/call on both platforms, since that
        // change touches the state-based overload, not this string-based one, so the budget is unchanged.
        GateValueTask(
            budgetBytes: 4224,
            action: () => client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days."),
            label: "TypedEvaluateRoundTrip",
            passDescription: "EvaluateAsync<T> stays within its allocation budget");
    }

    /// <summary><see cref="JevContent.FromValue{T}(T, System.Text.Json.Serialization.Metadata.JsonTypeInfo{T})"/> over the smoke state.</summary>
    public static void ContentFromValue()
    {
        var state = new SmokeState("Payouts failing", "Help! My payouts have been failing for 3 days.");

        // Measured 280 B/call on published win-x64 AOT: JsonSerializer.SerializeToElement serializes SmokeState's
        // Subject and Body strings and builds a JsonDocument over the result, which JevContent then wraps without
        // copying. A linux-x64 measurement (dotnet/sdk:10.0 container, 2026-09-28) matches exactly: 280 B/call. The
        // budget keeps about 10% headroom (320 B, rounded to the next 64 B) over that measurement, since
        // JsonDocument's internal buffer sizing can still differ across runtime patch versions on either platform.
        Gate(
            budgetBytes: 320,
            action: () => _ = JevContent.FromValue(state, SmokeStateJsonContext.Default.SmokeState),
            label: "ContentFromValue",
            passDescription: "JevContent.FromValue stays within its allocation budget");
    }

    /// <summary><see cref="JevContent.FromUtf8Json(ReadOnlySpan{byte})"/> over a fixed object.</summary>
    public static void ContentFromUtf8Json()
    {
        var json = Encoding.UTF8.GetBytes("""{"message":"Please send me your password","channel":"email"}""");

        // Measured 256 B/call on published win-x64 AOT: JsonElement.ParseValue parses the fixed JSON object into a
        // JsonDocument, which JevContent then wraps without copying. A linux-x64 measurement (dotnet/sdk:10.0
        // container, 2026-09-28) matches exactly: 256 B/call. The budget keeps about 10% headroom (320 B, rounded
        // to the next 64 B) over that measurement, for the same cross-platform, cross-patch-version reason as
        // ContentFromValue's gate.
        Gate(
            budgetBytes: 320,
            action: () => _ = JevContent.FromUtf8Json(json),
            label: "ContentFromUtf8Json",
            passDescription: "JevContent.FromUtf8Json stays within its allocation budget");
    }

    private static void Gate(int budgetBytes, Action action, string label, string passDescription)
    {
        try
        {
            AllocationGate.AssertBudget(budgetBytes, 1000, action, label);
            Program.Check(true, passDescription);
        }
        catch (InvalidOperationException exception)
        {
            Program.Check(false, exception.Message);
        }
    }

    private static void GateValueTask<T>(int budgetBytes, Func<ValueTask<T>> action, string label, string passDescription)
    {
        try
        {
            AllocationGate.AssertBudgetValueTask(budgetBytes, 1000, action, label);
            Program.Check(true, passDescription);
        }
        catch (InvalidOperationException exception)
        {
            Program.Check(false, exception.Message);
        }
    }

    /// <summary>Mirrors the generated option set for <see cref="Team"/>, since the real one is a private nested class.</summary>
    private sealed class TeamOptions : JevOptionSet<Team>
    {
        public static readonly TeamOptions Instance = new();

        public override int Count => 2;

        public override Team this[int index] => index switch
        {
            0 => Team.Billing,
            1 => Team.Account,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Team value) => value switch
        {
            Team.Billing => 0,
            Team.Account => 1,
            _ => -1,
        };

        public override int IndexOfKey(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals("billing"u8))
            {
                return 0;
            }

            if (reader.ValueTextEquals("account"u8))
            {
                return 1;
            }

            return -1;
        }
    }

    /// <summary>Mirrors the generated option set for <see cref="Urgency"/>, since the real one is a private nested class.</summary>
    private sealed class UrgencyOptions : JevOptionSet<Urgency>
    {
        public static readonly UrgencyOptions Instance = new();

        public override int Count => 3;

        public override Urgency this[int index] => index switch
        {
            0 => Urgency.Low,
            1 => Urgency.Medium,
            2 => Urgency.High,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };

        public override int IndexOf(Urgency value) => value switch
        {
            Urgency.Low => 0,
            Urgency.Medium => 1,
            Urgency.High => 2,
            _ => -1,
        };

        public override int IndexOfKey(ref Utf8JsonReader reader)
        {
            if (reader.ValueTextEquals("0"u8))
            {
                return 0;
            }

            if (reader.ValueTextEquals("1"u8))
            {
                return 1;
            }

            if (reader.ValueTextEquals("2"u8))
            {
                return 2;
            }

            return -1;
        }
    }
}
