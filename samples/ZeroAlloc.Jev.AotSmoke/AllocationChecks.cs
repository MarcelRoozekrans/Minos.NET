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
        // SystemOneResponse. This is the one gate whose allocation shape can vary across OS and runtime patch
        // versions, since HttpClient's internal buffering differs by platform, so it carries about 10% headroom
        // (5120 B, ~11% over the win-x64 measurement) instead of the next 64-byte step. Bring the budget down once
        // a linux-x64 measurement exists.
        GateValueTask(
            budgetBytes: 5120,
            action: () => client.EvaluateAsync(request),
            label: "EvaluateRoundTrip",
            passDescription: "EvaluateAsync stays within its allocation budget");
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
