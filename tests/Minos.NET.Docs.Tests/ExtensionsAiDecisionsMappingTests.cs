using System.Reflection;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The mapping page, docs/extensions-ai-decisions/mapping.md, names the types and members it compares. This test reads
// those names and checks them against both public APIs, so the page cannot drift from either.
//
// Page format:
//   - One "##" heading per area, with the exact text of AreaHeadings below. Each area has one table with at least one row.
//   - The first column of every row holds one or more code spans. Each span starts with "Minos: " or "Upstream: ",
//     then the identifier, for example `Minos: IDecisionClient.DecideAsync` or `Upstream: IDecisionClient`.
//     Prose such as "no counterpart" goes in the other columns, never in a code span of the first column.
//   - An identifier is "Type" or "Type.Member", with an optional namespace in front ("Minos.IDecisionClient.DecideAsync").
//     A type is found by its full name, or else by its simple name among the public types of the assembly; a generic
//     type is written without arity or type arguments ("ProbabilityMap", not "ProbabilityMap<T>"). A member is any
//     public method, property, field, event or nested type of that name, on the type or what it inherits.
//   - The Minos side is the Minos.NET assembly; the upstream side is the assembly named by MINOS_UPSTREAM_ABSTRACTIONS.
public sealed partial class ExtensionsAiDecisionsMappingTests
{
    private const string Page = "extensions-ai-decisions/mapping.md";

    /// <summary>The areas of the page, in order; each is a "##" heading with a table of rows.</summary>
    internal static readonly string[] AreaHeadings =
    [
        "Client interface and GetService",
        "Request, state and options",
        "Question kinds",
        "Answers and distributions",
        "Errors",
        "Metadata and capabilities",
        "Minos-only parts",
    ];

    [Fact]
    public void Every_area_has_rows_with_prefixed_identifiers()
    {
        var rows = ReadRows();

        foreach (var area in AreaHeadings)
        {
            Assert.True(rows.Exists(r => string.Equals(r.Area, area, StringComparison.Ordinal)), $"{Page} has at least one table row under '## {area}'.");
        }

        foreach (var row in rows)
        {
            Assert.True(row.Identifiers.Count > 0, $"{Page}, '{row.Area}': the first cell '{row.Cell}' has a code span starting with 'Minos: ' or 'Upstream: '.");
        }
    }

    [Fact]
    public void Minos_identifiers_are_public_in_Minos_NET()
    {
        var types = typeof(IDecisionClient).Assembly.GetExportedTypes().Select(t => (Type)t).ToList();
        var missing = Identifiers("Minos").Where(id => !Resolves(types, id)).ToList();

        Assert.True(missing.Count == 0, $"{Page} names Minos identifiers that are not public in Minos.NET: {string.Join(", ", missing)}.");
    }

    [UpstreamAbstractionsFact]
    public void Upstream_identifiers_are_public_in_the_pinned_build()
    {
        var path = Path.GetFullPath(Environment.GetEnvironmentVariable(UpstreamAbstractionsFactAttribute.Variable)!);
        Assert.True(File.Exists(path), $"{UpstreamAbstractionsFactAttribute.Variable} names an existing file: {path}.");

        var paths = new List<string>(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.dll"));
        paths.AddRange(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "*.dll"));
        using var context = new MetadataLoadContext(new PathAssemblyResolver(paths));
        var types = context.LoadFromAssemblyPath(path).GetExportedTypes().ToList();

        var missing = Identifiers("Upstream").Where(id => !Resolves(types, id)).ToList();

        Assert.True(missing.Count == 0, $"{Page} names upstream identifiers that are not public in the pinned build: {string.Join(", ", missing)}.");
    }

    private static List<string> Identifiers(string side) =>
        [.. ReadRows().SelectMany(r => r.Identifiers).Where(i => string.Equals(i.Side, side, StringComparison.Ordinal)).Select(i => i.Name).Distinct(StringComparer.Ordinal)];

    private static bool Resolves(List<Type> types, string identifier)
    {
        if (FindType(types, identifier) is not null)
        {
            return true;
        }

        var dot = identifier.LastIndexOf('.');
        if (dot < 0)
        {
            return false;
        }

        var type = FindType(types, identifier[..dot]);
        return type is not null && HasMember(type, identifier[(dot + 1)..]);
    }

    private static Type? FindType(List<Type> types, string name)
    {
        var byFullName = types.FirstOrDefault(t => string.Equals(StripArity(t.FullName!), name, StringComparison.Ordinal));
        if (byFullName is not null || name.Contains('.', StringComparison.Ordinal))
        {
            return byFullName;
        }

        return types.FirstOrDefault(t => string.Equals(StripArity(t.Name), name, StringComparison.Ordinal));
    }

    private static string StripArity(string name)
    {
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? name : name[..tick];
    }

    private static bool HasMember(Type type, string member)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMember(member, Flags | BindingFlags.DeclaredOnly).Length > 0)
            {
                return true;
            }
        }

        return type.GetInterfaces().Any(i => i.GetMember(member, Flags).Length > 0);
    }

    private static List<(string Area, string Cell, List<(string Side, string Name)> Identifiers)> ReadRows()
    {
        var path = Path.Combine(PublishedPages.Root, "docs", Page);
        Assert.True(File.Exists(path), $"The mapping page docs/{Page} exists.");

        var rows = new List<(string, string, List<(string, string)>)>();
        string? area = null;
        var tableLine = 0;
        foreach (var line in File.ReadAllLines(path))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                area = line[3..].Trim();
                tableLine = 0;
            }
            else if (area is not null && line.StartsWith('|'))
            {
                tableLine++;
                if (tableLine <= 2)
                {
                    continue;
                }

                var cell = line.Trim().Trim('|').Split('|')[0].Trim();
                var identifiers = new List<(string, string)>();
                foreach (Match m in PrefixedSpan().Matches(cell))
                {
                    identifiers.Add((m.Groups["side"].Value, m.Groups["name"].Value.Trim()));
                }

                rows.Add((area, cell, identifiers));
            }
        }

        return rows;
    }

    [GeneratedRegex("`(?<side>Minos|Upstream): (?<name>[^`]+)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrefixedSpan();
}
