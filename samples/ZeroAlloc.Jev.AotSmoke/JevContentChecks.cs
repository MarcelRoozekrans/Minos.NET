using System.Text.Json;

namespace ZeroAlloc.Jev.AotSmoke;

/// <summary><see cref="JevContent"/>'s factories, accessors and equality under Native AOT.</summary>
internal static class JevContentChecks
{
    [Covers("static ZeroAlloc.Jev.JevContent.FromString(string! text) -> ZeroAlloc.Jev.JevContent")]
    [Covers("ZeroAlloc.Jev.JevContent.TryGetString(out string? text) -> bool")]
    [Covers("ZeroAlloc.Jev.JevContent.JevContent() -> void")]
    public static void TextContentRoundTrips()
    {
        var text = JevContent.FromString("Does `message` ask for a credential?");
        var uninitialized = new JevContent();

        Program.Check(
            text.TryGetString(out var value)
                && string.Equals(value, "Does `message` ask for a credential?", StringComparison.Ordinal)
                && !uninitialized.TryGetString(out _)
                && uninitialized.ToString().Length == 0,
            "JevContent.FromString keeps its text, and uninitialized content has none, under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentNullException>(() => JevContent.FromString(null!)),
            "JevContent.FromString rejects null under Native AOT");
    }

    [Covers("static ZeroAlloc.Jev.JevContent.FromJson(System.Text.Json.JsonElement json) -> ZeroAlloc.Jev.JevContent")]
    [Covers("ZeroAlloc.Jev.JevContent.TryGetJson(out System.Text.Json.JsonElement json) -> bool")]
    [Covers("ZeroAlloc.Jev.JevContent.Equals(ZeroAlloc.Jev.JevContent other) -> bool")]
    public static void JsonContentRoundTrips()
    {
        using var document = JsonDocument.Parse("""{"question":"Does `message` ask for a credential?","policy":{"strict":true}}""");
        using var reordered = JsonDocument.Parse("""{"policy":{"strict":true},"question":"Does `message` ask for a credential?"}""");
        using var text = JsonDocument.Parse("\"Does `message` ask for a credential?\"");
        using var number = JsonDocument.Parse("42");
        var json = JevContent.FromJson(document.RootElement);

        Program.Check(
            json.TryGetJson(out var element)
                && element.GetProperty("policy").GetProperty("strict").GetBoolean()
                && !json.TryGetString(out _)
                && JevContent.FromJson(text.RootElement).TryGetString(out var fromString)
                && string.Equals(fromString, "Does `message` ask for a credential?", StringComparison.Ordinal),
            "JevContent.FromJson keeps a JSON object and unwraps a JSON string under Native AOT");
        Program.Check(
            json.Equals(JevContent.FromJson(reordered.RootElement))
                && !json.Equals(JevContent.FromString("Does `message` ask for a credential?"))
                && JevContent.FromString("a").Equals(JevContent.FromString("a"))
                && new JevContent().Equals(default),
            "JevContent.Equals compares JSON deeply and text ordinally under Native AOT");
        Program.Check(
            SmokeAssert.Throws<ArgumentException>(() => JevContent.FromJson(number.RootElement)),
            "JevContent.FromJson rejects a JSON number under Native AOT");
    }
}
