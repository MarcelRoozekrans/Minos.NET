using System.Globalization;
using Minos.Extensions.AI;

namespace Minos.Docs.Tests;

// Keeps the chat-clients page in step with the bridge: its mode and source tables list the enums' values in order, and
// its capability table is what the bridge resolves by default.
public sealed class ChatClientsPageTests
{
    private const string Page = "chat-clients.md";

    [Fact]
    public void TheModeTable_IsProbabilityMode_InOrder()
        => Assert.Equal(Enum.GetNames<ProbabilityMode>(), PageTables.Rows(Page, "Probability modes").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void TheSourceTable_IsProbabilitySource_InOrder()
        => Assert.Equal(Enum.GetNames<ProbabilitySource>(), PageTables.Rows(Page, "Where the probabilities came from").Select(row => PageTables.Code(row[0])));

    [Fact]
    public void TheModeTable_NamesAutoAsTheDefault()
    {
        var auto = PageTables.Rows(Page, "Probability modes")[0];

        Assert.Equal(ProbabilityMode.Auto, new ChatDecisionClientOptions().ProbabilityMode);
        Assert.Equal(nameof(ProbabilityMode.Auto), PageTables.Code(auto[0]));
        Assert.StartsWith("The default.", auto[1], StringComparison.Ordinal);
    }

    [Fact]
    public void TheCapabilityTable_IsWhatTheBridgeResolves()
    {
        var rows = PageTables.Rows(Page, "Capabilities");
        using var client = new SilentChatClient().AsDecisionClient();
        var capabilities = Assert.IsType<DecisionCapabilities>(client.GetService(typeof(DecisionCapabilities)));

        Assert.Equal(["Question kinds", "Most questions", "Most Choice options", "Images", "Model listing"], rows.Select(row => row[0]));
        Assert.Equal(QuestionKinds.All, capabilities.QuestionKinds);
        Assert.Equal("every kind", rows[0][1]);
        Assert.Equal(Limit(rows[1][1]), capabilities.MaxQuestions);
        Assert.Equal(Limit(rows[2][1]), capabilities.MaxChoiceOptions);
        Assert.Equal("no", rows[3][1]);
        Assert.False(capabilities.SupportsImages);
        Assert.Equal("no", rows[4][1]);
        Assert.False(capabilities.SupportsModelListing);
    }

    private static int? Limit(string cell)
        => string.Equals(cell, "none", StringComparison.Ordinal) ? null : int.Parse(cell, CultureInfo.InvariantCulture);
}
