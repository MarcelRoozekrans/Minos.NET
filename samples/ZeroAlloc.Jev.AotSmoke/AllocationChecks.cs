using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ZeroAlloc.Jev.Shared;
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
    private const string ScoreAnswerJson = """{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}""";
    private const string TriageAnswersJson = """{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";
    private const string NoulResponseJson = """{"model":"jev-1.13.0","answers":{"is_urgent":{"type":"noul","noul":0.95}},"usage":{"input_tokens":296,"output_tokens":20}}""";
    private const string TriageResponseJson = """{"model":"jev-1.13.0","answers":{"requests_credentials":{"type":"noul","noul":0.1},"team":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.8},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}},"usage":{"input_tokens":296,"output_tokens":20}}""";

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

        // Measured ~4312 B/call on published win-x64 AOT: HttpRequestMessage, headers and content for the request,
        // plus the response's HttpResponseMessage, its body buffering and JSON deserialization into
        // SystemOneResponse. It measured higher, 4592 B/call, when first budgeted in Phase 1.8, on win-x64 and on
        // linux-x64 (dotnet/sdk:10.0 container, 2026-09-27). The budget stays at 5120 B, the Phase 1.8 figure with
        // about 10% headroom, since HttpClient's internal buffering can still differ across runtime patch versions
        // on either platform; tightening it is a separate decision.
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

        // Measured ~3368 B/call on published win-x64 AOT: the request's Utf8JsonWriter and RawJson, plus
        // ZeroAlloc.Rest's own per-attempt allocations (HttpRequestMessage, headers, the MemoryStream the body is
        // copied into, and StreamContent), plus the response's HttpResponseMessage and body buffering, and the
        // async state machines. There is no SystemOneRequest, SystemOneResponse, JevAnswer or questions dictionary
        // on this path, which is why it comes in well below EvaluateRoundTrip's 4312 B measurement. It measured
        // higher, 3784 B/call, when first budgeted in Phase 1.8 and re-measured in Phase 2.1, on win-x64 and on
        // linux-x64 (dotnet/sdk:10.0 container, 2026-09-27). The budget stays at 4224 B, the 3784 B figure with about
        // 10% headroom rounded to the next 64 B, for the same cross-platform, cross-patch-version reason as
        // EvaluateRoundTrip's gate; tightening it is a separate decision.
        GateValueTask(
            budgetBytes: 4224,
            action: () => client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days."),
            label: "TypedEvaluateRoundTrip",
            passDescription: "EvaluateAsync<T> stays within its allocation budget");
    }

    /// <summary><see cref="EvaluateRoundTrip"/> through <see cref="NullLoggerFactory"/>, whose logger has every level disabled.</summary>
    public static void EvaluateRoundTripWithNullLoggerFactory()
    {
        // Budget: EvaluateRoundTrip's own, 5120 B, unchanged. With no level enabled the client takes the unlogged path:
        // no logging wrapper and no LoggingJevApi state machine, so nothing may be added.
        EvaluateRoundTripThrough(
            NullLoggerFactory.Instance,
            budgetBytes: 5120,
            "EvaluateRoundTripWithNullLoggerFactory",
            "EvaluateAsync through NullLoggerFactory stays within EvaluateRoundTrip's budget");
    }

    /// <summary>
    /// <see cref="TypedEvaluateRoundTrip"/> through a real <see cref="LoggerFactory"/> with a provider, whose filter
    /// disables every level.
    /// </summary>
    public static void TypedEvaluateRoundTripWithEveryLevelFiltered()
    {
        using var provider = new CapturingLoggerProvider();
        using var factory = new LoggerFactory([provider], new LoggerFilterOptions { MinLevel = LogLevel.None });

        // Budget: TypedEvaluateRoundTrip's own, 4224 B, unchanged, for the same reason as the NullLoggerFactory gate.
        TypedEvaluateRoundTripThrough(
            factory,
            budgetBytes: 4224,
            "TypedEvaluateRoundTripWithEveryLevelFiltered",
            "EvaluateAsync<T> with every level filtered out stays within TypedEvaluateRoundTrip's budget");
        Program.Check(provider.Records.Length == 0, "a logger with every level filtered out receives no record");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTrip"/> through a logger enabled at every level that discards everything, so every log
    /// call, timestamp and wrapper runs.
    /// </summary>
    public static void EvaluateRoundTripWithDiscardingLogger()
    {
        var callsBefore = DiscardingLoggerFactory.Calls;

        // Measured ~4312 B/call on published win-x64 AOT, the same as EvaluateRoundTrip's current measurement and its unlogged twin in this run,
        // so the enabled logger adds nothing on the synchronous path the canned handler takes: each event's state is a
        // struct handed to a logger that discards it, the timing is two Stopwatch timestamps, and no state machine is
        // boxed while a call completes synchronously. A call that completes asynchronously also allocates the
        // LoggingJevApi and LogEvaluationAsync state machines, which this gate cannot see. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        EvaluateRoundTripThrough(
            DiscardingLoggerFactory.Instance,
            budgetBytes: 4800,
            "EvaluateRoundTripWithDiscardingLogger",
            "EvaluateAsync with every log level enabled stays within its budget");
        Program.Check(
            DiscardingLoggerFactory.Calls > callsBefore,
            "the discarding logger received log calls, so EvaluateAsync ran the logged path");
    }

    /// <summary><see cref="TypedEvaluateRoundTrip"/> through a logger enabled at every level that discards everything.</summary>
    public static void TypedEvaluateRoundTripWithDiscardingLogger()
    {
        var callsBefore = DiscardingLoggerFactory.Calls;

        // Measured ~3368 B/call on published win-x64 AOT, the same as TypedEvaluateRoundTrip's current measurement and its unlogged twin in this run,
        // so the enabled logger adds nothing on the synchronous path the canned handler takes: each event's state is a
        // struct handed to a logger that discards it, the timing is two Stopwatch timestamps, and no state machine is
        // boxed while a call completes synchronously. A call that completes asynchronously also allocates the
        // LoggingJevApi and LogEvaluationAsync state machines, which this gate cannot see. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        TypedEvaluateRoundTripThrough(
            DiscardingLoggerFactory.Instance,
            budgetBytes: 3712,
            "TypedEvaluateRoundTripWithDiscardingLogger",
            "EvaluateAsync<T> with every log level enabled stays within its budget");
        Program.Check(
            DiscardingLoggerFactory.Calls > callsBefore,
            "the discarding logger received log calls, so EvaluateAsync<T> ran the logged path");
    }

    /// <summary>
    /// Proves the disabled-logger gates can tell a disabled logger from an enabled one. The canned handler completes
    /// synchronously, where even an enabled logger adds nothing, so those gates would pass if a disabled logger wrongly
    /// entered the logging wrappers. A yielding handler makes every call complete asynchronously, where the wrappers'
    /// state machines are boxed and show up as bytes.
    /// </summary>
    public static async Task DisabledLoggerAddsNothingWhereAnEnabledOneDoes()
    {
        var request = Program.Request();
        var unlogged = await MedianYieldingAsync(NoulResponseJson, null, client => client.EvaluateAsync(request)).ConfigureAwait(false);
        var disabled = await MedianYieldingAsync(NoulResponseJson, NullLoggerFactory.Instance, client => client.EvaluateAsync(request)).ConfigureAwait(false);
        var enabled = await MedianYieldingAsync(NoulResponseJson, DiscardingLoggerFactory.Instance, client => client.EvaluateAsync(request)).ConfigureAwait(false);
        Console.WriteLine($"     yielding EvaluateAsync B/call: no factory {unlogged}, NullLoggerFactory {disabled}, discarding logger {enabled}");

        // The tolerance absorbs measurement noise: a single run can sit tens of bytes per call above or below the usual
        // figure, which the median of five runs removes, leaving a few bytes. A wrapper's state machine is hundreds of
        // bytes per call.
        Program.Check(
            disabled - unlogged <= 8,
            "a NullLoggerFactory adds no allocation to an asynchronously completing EvaluateAsync");
        Program.Check(
            enabled - unlogged > 0,
            "the discarding logger adds allocation to an asynchronously completing EvaluateAsync, so this check sees the wrappers");
    }

    // The median of five runs. Yielding runs vary in both directions: a runtime thread allocating during the loop adds
    // bytes, and a continuation that lands where a pooled buffer is still cached saves some. The least of several runs
    // is therefore biased low and lets one lucky run set a baseline; the median ignores an outlier on either side.
    private static async Task<long> MedianYieldingAsync<TResult>(
        string responseJson, ILoggerFactory? loggerFactory, Func<JevClient, ValueTask<TResult>> call)
    {
        const int Runs = 5;
        var runs = new List<long>(Runs);
        for (var run = 0; run < Runs; run++)
        {
            runs.Add(await MeasureYieldingAsync(responseJson, loggerFactory, call).ConfigureAwait(false));
        }

        runs.Sort();
        return runs[Runs / 2];
    }

    // Bytes allocated per awaited call over a handler that yields, on any thread, since the continuation does not run on
    // the caller's. The loop is sequential, so nothing else allocates meanwhile but the runtime. call is created once by
    // the caller, so invoking it allocates nothing per call.
    private static async Task<long> MeasureYieldingAsync<TResult>(
        string responseJson, ILoggerFactory? loggerFactory, Func<JevClient, ValueTask<TResult>> call)
    {
        const int YieldingIterations = 500;
        using var http = new HttpClient(new YieldingHandler(HttpStatusCode.OK, responseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" }, loggerFactory);

        for (var i = 0; i < 100; i++)
        {
            _ = await call(client).ConfigureAwait(false);
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        var before = GC.GetTotalAllocatedBytes(precise: true);
        for (var i = 0; i < YieldingIterations; i++)
        {
            _ = await call(client).ConfigureAwait(false);
        }

        return (GC.GetTotalAllocatedBytes(precise: true) - before) / YieldingIterations;
    }

    // EvaluateRoundTrip's call, the same canned response and Program.Request(), through a logging client.
    private static void EvaluateRoundTripThrough(ILoggerFactory loggerFactory, int budgetBytes, string label, string passDescription)
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" }, loggerFactory);
        var request = Program.Request();

        GateValueTask(budgetBytes, () => client.EvaluateAsync(request), label, passDescription);
    }

    // TypedEvaluateRoundTrip's call, the same canned response and state, through a logging client.
    private static void TypedEvaluateRoundTripThrough(ILoggerFactory loggerFactory, int budgetBytes, string label, string passDescription)
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" }, loggerFactory);

        GateValueTask(
            budgetBytes,
            () => client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days."),
            label,
            passDescription);
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

    /// <summary><see cref="JevQuestionSetBuilder.Build"/> over a three-question set: a Noul, an enum Choice and a keyed Choice.</summary>
    public static void BuildQuestionSet()
    {
        var builder = SmokeBuiltSet.Builder(out _, out _, out _);

        // Measured 6592 B/call on published win-x64 AOT: the builder's question and warning lists, the Utf8JsonWriter and
        // its ArrayBufferWriter growth, the UTF-8 keys and the resulting JevQuestionSet. Budget: about 10% headroom, 7251 B,
        // rounded up to the next multiple of 64, 7296 B, since writer growth and list capacities follow runtime internals.
        Gate(
            budgetBytes: 7296,
            action: () => _ = builder.Build(),
            label: "BuildQuestionSet",
            passDescription: "JevQuestionSetBuilder.Build stays within its allocation budget");
    }

    /// <summary><see cref="JevClient.EvaluateAsync(JevQuestionSet, JevContent)"/> over a canned handler.</summary>
    public static void EvaluateBuiltSetRoundTrip()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out _, out _, out _, out _);

        // Measured 3656 B/call on published win-x64 AOT: the JevQuestionSet's pre-built request body copied into a pooled
        // buffer, HttpClient's request and response objects and body buffering, the JevAnswers result and the async state
        // machines if the call does not complete synchronously. It measured higher, 4288 B/call, when first budgeted in
        // Phase 2.4. The budget stays at 4736 B, that figure with about 10% headroom rounded up to the next multiple of
        // 64, since HttpClient's allocations follow runtime internals; tightening it is a separate decision.
        GateValueTask(
            budgetBytes: 4736,
            action: () => client.EvaluateAsync(set, "Help! My payouts have been failing for 3 days."),
            label: "EvaluateBuiltSetRoundTrip",
            passDescription: "EvaluateAsync over a built set stays within its allocation budget");
    }

    private static double sink;

    /// <summary><see cref="JevAnswers.Get(NoulHandle)"/> and its overloads, over one evaluation's answers.</summary>
    public static void JevAnswersGet()
    {
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson))
        {
            BaseAddress = new Uri("https://example.test/api/"),
        };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out var credentials, out var team, out var product, out var urgency);
        var answers = client.EvaluateAsync(set, "Help!").AsTask().GetAwaiter().GetResult().Value;

        // 0 B/call: Get rebuilds each typed answer as a struct over the answers' shared probability buffer.
        Gate(
            budgetBytes: 0,
            action: () =>
            {
                var noul = answers.Get(credentials);
                var teamAnswer = answers.Get(team);
                var productAnswer = answers.Get(product);
                var urgencyAnswer = answers.Get(urgency);

                // Consumed into a static field so the compiler cannot elide the calls and make the 0 B gate vacuous.
                sink += (noul.Value ? 1 : 0) + (int)teamAnswer.Value + productAnswer.Value.Length + (int)urgencyAnswer.Value
                    + teamAnswer.Confidence + productAnswer.Confidence + urgencyAnswer.Confidence + urgencyAnswer.Expected;
            },
            label: "JevAnswersGet",
            passDescription: "JevAnswers.Get allocates nothing");
    }

    /// <summary>
    /// <see cref="EvaluateRoundTrip"/> while discarding listeners sample every span and enable every instrument.
    /// </summary>
    public static void EvaluateRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, NoulResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });
        var request = Program.Request();

        // Measured 5680 B/call on published win-x64 AOT. The call pays EvaluateRoundTrip's bytes plus the Activity, its
        // boxed start tags, the boxed tag and measurement values and each metric's TagList. Budget: about 10% headroom
        // over the measurement, rounded up to the next multiple of 64 B, per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 6272,
            action: () => client.EvaluateAsync(request),
            label: "EvaluateRoundTripWhileListening",
            passDescription: "EvaluateAsync while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so EvaluateAsync ran the listening path");
    }

    /// <summary><see cref="TypedEvaluateRoundTrip"/> while discarding listeners are attached.</summary>
    public static void TypedEvaluateRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, TriageResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });

        // Measured 4928 B/call on published win-x64 AOT. The call pays TypedEvaluateRoundTrip's bytes plus the span, tags
        // and measurements, and the deferred reads, which allocate only the response model's string, once per attribute
        // that reads it. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B, per the
        // Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5440,
            action: () => client.EvaluateAsync<SmokeTriage>("Help! My payouts have been failing for 3 days."),
            label: "TypedEvaluateRoundTripWhileListening",
            passDescription: "EvaluateAsync<T> while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so EvaluateAsync<T> ran the listening path");
    }

    /// <summary><see cref="EvaluateBuiltSetRoundTrip"/> while discarding listeners are attached.</summary>
    public static void EvaluateBuiltSetRoundTripWhileListening()
    {
        using var telemetry = new DiscardingTelemetry();
        using var http = new HttpClient(new CannedHandler(HttpStatusCode.OK, SmokeBuiltSet.ResponseJson)) { BaseAddress = new Uri("https://example.test/api/") };
        using var client = new JevClient(http, new JevClientOptions { ApiKey = "smoke-key" });
        var set = SmokeBuiltSet.Full(out _, out _, out _, out _);

        // Measured 5216 B/call on published win-x64 AOT. The call pays EvaluateBuiltSetRoundTrip's bytes plus the span,
        // tags and measurements. Budget: about 10% headroom over the measurement, rounded up to the next multiple of 64 B,
        // per the Phase 1.8 rule.
        GateValueTask(
            budgetBytes: 5760,
            action: () => client.EvaluateAsync(set, "Help! My payouts have been failing for 3 days."),
            label: "EvaluateBuiltSetRoundTripWhileListening",
            passDescription: "EvaluateAsync over a built set while listening stays within its allocation budget");
        Program.Check(telemetry.Measurements > 0, "the discarding listeners received measurements, so the built set ran the listening path");
    }

    /// <summary>
    /// A typed evaluation that completes asynchronously, with nothing listening: the one place telemetry adds bytes when
    /// off, the unwrap's state machine. The canned-handler gates complete synchronously and cannot see it.
    /// </summary>
    public static async Task TelemetryOffAsynchronousTypedEvaluation()
    {
        var median = await MedianYieldingAsync(TriageResponseJson, null, static client => client.EvaluateAsync<SmokeTriage>("Help!")).ConfigureAwait(false);
        Console.WriteLine($"     yielding EvaluateAsync<T> B/call with telemetry off: {median}");

        // Measured 4568 B/call, the median of five runs, on published win-x64 AOT. This is the whole asynchronously
        // completing call, not only the unwrap: the transport's request and response, body buffering, parsing and every
        // boxed async state machine, among them the unwrap's. Budget: about 10% headroom over the measurement, rounded up
        // to the next multiple of 64 B, per the Phase 1.8 rule.
        const long BudgetBytes = 5056;
        Program.Check(
            median <= BudgetBytes,
            $"an asynchronously completing EvaluateAsync<T> with telemetry off stays within its allocation budget: {median} B/call against {BudgetBytes} B");
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
