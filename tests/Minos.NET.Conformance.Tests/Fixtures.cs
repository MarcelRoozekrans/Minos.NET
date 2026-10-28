namespace Minos.Conformance.Tests;

/// <summary>Where the fixtures are: copied next to the tests, and in the repository for the scans.</summary>
internal static class Fixtures
{
    public const string ReplayKey = "replay-key";

    public static string Path(string target) => System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", target + ".json");

    public static string SourceDirectory { get; } = System.IO.Path.Combine(Root(), "tests", "Minos.NET.Conformance.Tests", "Fixtures");

    public static ConformanceTarget Replay(string target) => target switch
    {
        "typesafe" => SystemOneTargets.TypeSafe(ReplayKey, maxRetries: 0),
        "openrouter" => SystemOneTargets.OpenRouter(ReplayKey, maxRetries: 0),
        "openai" => OpenAITargets.OpenAI(ReplayKey, maxRetries: 0),
        ChatTargets.OpenRouterName => ChatTargets.OpenRouter(ChatTargets.DefaultModel, maxRetries: 0, ReplayKey),
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "No such target."),
    };

    private static string Root()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Minos.NET.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }
}
