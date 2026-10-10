using System.Text;
using System.Text.Json;
using Minos.Protocols;
using Minos.Tests.WireFixtureSets;
using ZeroAlloc.TestHelpers;

namespace Minos.Tests;

/// <summary>The allocation budget for the protocol reading a generated set's answers into its typed result.</summary>
public sealed class GeneratedSetAllocationTests
{
    private const string AnswersJson = """{"requests_credentials":{"type":"noul","noul":0.1},"route_to":{"type":"choice","choice":"account","probabilities":{"billing":0.2,"account":0.7,"other":0.1},"confidence":0.7},"urgency":{"type":"score","score":1.9,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.0,"1":0.1,"2":0.9},"confidence":0.8}}""";

    // One factory for every read, as the typed client path keeps, so the gate measures the read and not a delegate.
    private static readonly AnswerFactory<WfMixed> Factory = static answers => WfMixed.Create(answers);

    [Fact]
    public void ReadingAGeneratedSetsAnswers_StaysWithinTheOldGeneratedParseBudget()
    {
        var answers = Encoding.UTF8.GetBytes(AnswersJson);

        // The old generated Parse held the smoke triage to 192 B (measured 176 B). The protocol's probability buffer and
        // the generated Create's result are the same work, so the budget is the same. WfMixed measures 184 B/call here.
        AllocationGate.AssertBudget(
            192,
            1000,
            () =>
            {
                var reader = new Utf8JsonReader(answers);
                reader.Read();
                _ = SystemOneProtocol.Instance.ReadAnswers(ref reader, WfMixed.Definition, Factory);
            },
            "ProtocolReadAnswers");
    }
}
