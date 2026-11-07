using System.Text.Json.Nodes;

namespace Minos.Docs.Tests;

// Keeps the Escalation category in its place in the sidebar. Later recipe pages add their own tests here.
public sealed class EscalationPageTests
{
    [Fact]
    public void TheCategory_SitsBeforePatterns()
    {
        Assert.Equal(18, CategoryPosition("escalation"));
        Assert.Equal(19, CategoryPosition("patterns"));
    }

    [Fact]
    public void TheIndex_IsPositionOne()
    {
        var front = PublishedPages.FrontMatter("docs/escalation/index.md");

        Assert.Equal("escalation", front["id"]);
        Assert.Equal("1", front["sidebar_position"]);
    }

    private static int CategoryPosition(string folder) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(PublishedPages.Root, "docs", folder, "_category_.json")))!["position"]!.GetValue<int>();
}
