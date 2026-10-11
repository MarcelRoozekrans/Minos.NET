using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

/// <summary>The rule ids of <c>DiagnosticIds.cs</c>, read from source, for the checks that compare a page with them.</summary>
internal static partial class DiagnosticIdSource
{
    /// <summary>Every <c>MIN</c> constant, by its constant name.</summary>
    public static Dictionary<string, string> ByName()
    {
        var source = File.ReadAllText(Path.Combine(PublishedPages.Root, "src", "Minos.NET.Generator", "DiagnosticIds.cs"));
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match match in IdConstant().Matches(source))
        {
            ids[match.Groups["name"].Value] = match.Groups["id"].Value;
        }

        return ids;
    }

    /// <summary>The provider rules the client checks at run time: every <c>MIN2xx</c> id, in order.</summary>
    public static List<string> ProviderRules()
        => [.. ByName().Values.Where(id => id.StartsWith("MIN2", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"public const string (?<name>\w+) = ""(?<id>MIN\d+)"";", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex IdConstant();
}
