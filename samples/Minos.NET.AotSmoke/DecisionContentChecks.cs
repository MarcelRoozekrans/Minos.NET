using System.Text.Json;

namespace Minos.AotSmoke;

/// <summary><see cref="DecisionContent"/>'s factories, accessors and equality under Native AOT.</summary>
internal static class DecisionContentChecks
{
    [Covers("static Minos.DecisionContent.FromString(string! text) -> Minos.DecisionContent")]
    [Covers("Minos.DecisionContent.TryGetString(out string? text) -> bool")]
    [Covers("Minos.DecisionContent.DecisionContent() -> void")]
    public static void TextContentRoundTrips()
    {
        var text = DecisionContent.FromString("Does `message` ask for a credential?");
        var uninitialized = new DecisionContent();

        Program.Check(
            text.TryGetString(out var value)
                && string.Equals(value, "Does `message` ask for a credential?", StringComparison.Ordinal)
                && !uninitialized.TryGetString(out _)
                && uninitialized.ToString().Length == 0,
            "DecisionContent.FromString keeps its text, and uninitialized content has none, under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => DecisionContent.FromString(null!)),
            "DecisionContent.FromString rejects null under Native AOT");
    }

    [Covers("static Minos.DecisionContent.FromJson(System.Text.Json.JsonElement json) -> Minos.DecisionContent")]
    [Covers("Minos.DecisionContent.TryGetJson(out System.Text.Json.JsonElement json) -> bool")]
    [Covers("Minos.DecisionContent.Equals(Minos.DecisionContent other) -> bool")]
    public static void JsonContentRoundTrips()
    {
        using var document = JsonDocument.Parse("""{"question":"Does `message` ask for a credential?","policy":{"strict":true}}""");
        using var reordered = JsonDocument.Parse("""{"policy":{"strict":true},"question":"Does `message` ask for a credential?"}""");
        using var text = JsonDocument.Parse("\"Does `message` ask for a credential?\"");
        using var number = JsonDocument.Parse("42");
        var json = DecisionContent.FromJson(document.RootElement);

        Program.Check(
            json.TryGetJson(out var element)
                && element.GetProperty("policy").GetProperty("strict").GetBoolean()
                && !json.TryGetString(out _)
                && DecisionContent.FromJson(text.RootElement).TryGetString(out var fromString)
                && string.Equals(fromString, "Does `message` ask for a credential?", StringComparison.Ordinal),
            "DecisionContent.FromJson keeps a JSON object and unwraps a JSON string under Native AOT");
        Program.Check(
            json.Equals(DecisionContent.FromJson(reordered.RootElement))
                && !json.Equals(DecisionContent.FromString("Does `message` ask for a credential?"))
                && DecisionContent.FromString("a").Equals(DecisionContent.FromString("a"))
                && new DecisionContent().Equals(default),
            "DecisionContent.Equals compares JSON deeply and text ordinally under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => DecisionContent.FromJson(number.RootElement)),
            "DecisionContent.FromJson rejects a JSON number under Native AOT");
    }
}
