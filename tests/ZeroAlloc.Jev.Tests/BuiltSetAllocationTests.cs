using System.Text;
using System.Text.Json;
using ZeroAlloc.TestHelpers;

namespace ZeroAlloc.Jev.Tests;

/// <summary>The allocation budget for parsing a built set's answers.</summary>
public sealed class BuiltSetAllocationTests
{
    private const string AnswersJson = """{"is_urgent":{"type":"noul","noul":0.95},"department":{"type":"choice","choice":"billing","probabilities":{"billing":0.88,"technical":0.12},"confidence":0.81},"effort":{"type":"score","score":1.2,"legend":{"0":"Low","1":"Medium","2":"High"},"probabilities":{"0":0.1,"1":0.6,"2":0.3},"confidence":0.7}}""";

    [Fact]
    public void ParsingABuiltSetsAnswers_StaysWithinItsBudget()
    {
        var built = JevQuestionSet.CreateBuilder()
            .Noul("is_urgent", "Does this convey urgency?", out _)
            .Choice<Department>("department", "Which team?", out _)
            .Score("effort", "How much effort?", out _, l => l.Level("Minutes").Level("Hours").Level("Days"))
            .Build();
        Assert.True(built.IsSuccess);
        var set = built.Value;
        var answers = Encoding.UTF8.GetBytes(AnswersJson);

        // Measured 216 B/call under the JIT on win-x64: the JevAnswers object, its double[7] probability buffer and its
        // AnswerSlot[3]. Budget: only fixed-layout objects, so the measurement rounded up to the next multiple of 64, 256 B.
        AllocationGate.AssertBudget(
            256,
            1000,
            () =>
            {
                var reader = new Utf8JsonReader(answers);
                reader.Read();
                _ = set.Parse(ref reader);
            },
            "ParseBuiltSet");
    }
}
