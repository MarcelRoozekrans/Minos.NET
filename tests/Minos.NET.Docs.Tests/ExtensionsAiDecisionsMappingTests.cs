using System.Reflection;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The mapping page, docs/extensions-ai-decisions/mapping.md, names the types and members it compares. This test reads
// those names and checks them against both public APIs, so the page cannot drift from either.
//
// Page format:
//   - One "##" heading per area, with the exact text of AreaHeadings below. Each area has exactly ONE table, made of
//     contiguous lines starting with "|", with the header "| Minos | Upstream | Fit | Adapter |", then a separator row.
//   - Column 1 holds code spans "Minos: X", column 2 holds code spans "Upstream: Y", for example
//     `Minos: IDecisionClient.Foo`. An empty cell or an em dash means no counterpart on that side.
//   - Fit is one of "one to one", "with a loss" or "no counterpart". A "no counterpart" row has exactly one side;
//     any other row has both a Minos and an Upstream identifier.
//   - An identifier is "Type" or "Type.Member", with an optional namespace in front. A type is found by its full name,
//     or else by its simple name among the public types of the side's assemblies. "Name<A, B>" names the type of that
//     name with two type parameters; the parameter names are free, their count is the arity. A plain name names the
//     non-generic type of that name when there is one, so "DecisionResponse" is the non-generic upstream type and
//     "DecisionResponse<TResult>" the generic one; with no non-generic namesake, a plain name names the one generic type,
//     as "Score" names Score<T>. A name that still matches more than one type, such as the same name in two namespaces
//     or two generic arities, fails the test as ambiguous. A member is any public method, property, field, event or
//     nested type of that name, on the type or what it inherits.
//   - The Minos side is the Minos.NET, Minos.NET.Caching and Minos.NET.Recording assemblies; the upstream side is the
//     assembly named by MINOS_UPSTREAM_ABSTRACTIONS.
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

    private const string NoCounterpart = "no counterpart";

    private static readonly string[] Fits = ["one to one", "with a loss", NoCounterpart];

    private static readonly string[] Header = ["Minos", "Upstream", "Fit", "Adapter"];

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
            var where = $"{Page}, '{row.Area}', row '{row.Minos} | {row.Upstream}'";
            Assert.True(Array.IndexOf(Fits, row.Fit) >= 0, $"{where}: Fit '{row.Fit}' must be one of {string.Join(", ", Fits)}.");
            var sides = (row.MinosNames.Count > 0 ? 1 : 0) + (row.UpstreamNames.Count > 0 ? 1 : 0);
            Assert.True(
                string.Equals(row.Fit, NoCounterpart, StringComparison.Ordinal) ? sides == 1 : sides == 2,
                $"{where}: a '{NoCounterpart}' row has exactly one side, any other fit has both a Minos and an Upstream identifier.");
        }
    }

    [Fact]
    public void Minos_identifiers_are_public_in_Minos_NET()
    {
        var types = MinosAssemblies.SelectMany(a => a.GetExportedTypes()).ToList();
        var ambiguous = new List<string>();
        var missing = Identifiers("Minos").Where(id => !Resolves(types, id, ambiguous)).ToList();

        Assert.True(ambiguous.Count == 0, $"{Page} names Minos types that match more than one public type; write them with their type parameters: {string.Join(", ", ambiguous)}.");
        Assert.True(missing.Count == 0, $"{Page} names Minos identifiers that are not public in Minos.NET, Minos.NET.Caching or Minos.NET.Recording: {string.Join(", ", missing)}.");
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

        var ambiguous = new List<string>();
        var missing = Identifiers("Upstream").Where(id => !Resolves(types, id, ambiguous)).ToList();

        Assert.True(ambiguous.Count == 0, $"{Page} names upstream types that match more than one public type; write them with their type parameters: {string.Join(", ", ambiguous)}.");
        Assert.True(missing.Count == 0, $"{Page} names upstream identifiers that are not public in the pinned build: {string.Join(", ", missing)}.");
    }

    private static List<string> Identifiers(string side) =>
        [.. ReadRows().SelectMany(r => string.Equals(side, "Minos", StringComparison.Ordinal) ? r.MinosNames : r.UpstreamNames).Distinct(StringComparer.Ordinal)];

    /// <summary>The assemblies whose public types the Minos side of the page may name.</summary>
    private static readonly Assembly[] MinosAssemblies =
    [
        typeof(IDecisionClient).Assembly,
        typeof(CachingDecisionClient).Assembly,
        typeof(Minos.Recording.RecordingDecisionClient).Assembly,
    ];

    private static bool Resolves(List<Type> types, string identifier, List<string> ambiguous)
    {
        if (FindType(types, identifier, ambiguous) is not null)
        {
            return true;
        }

        var dot = identifier.LastIndexOf('.');
        if (dot < 0 || identifier.EndsWith('>'))
        {
            return false;
        }

        var type = FindType(types, identifier[..dot], ambiguous);
        return type is not null && HasMember(type, identifier[(dot + 1)..]);
    }

    /// <summary>
    /// Finds the one type a name stands for. "Name&lt;A, B&gt;" matches only a type of that name with two type parameters.
    /// A plain name matches the non-generic type of that name when there is one, and otherwise a generic type of any
    /// arity. More than one match, such as the same name in two namespaces or two generic arities, is ambiguous.
    /// </summary>
    private static Type? FindType(List<Type> types, string name, List<string> ambiguous)
    {
        int? arity = null;
        var open = name.IndexOf('<', StringComparison.Ordinal);
        if (open >= 0)
        {
            if (!name.EndsWith('>'))
            {
                return null;
            }

            arity = name[(open + 1)..^1].Split(',').Length;
            name = name[..open];
        }

        bool Matches(Type t, string candidate) =>
            string.Equals(StripArity(candidate), name, StringComparison.Ordinal) && (arity is null || Arity(t) == arity);

        var byFullName = types.Where(t => Matches(t, t.FullName!)).ToList();
        var found = byFullName.Count > 0 || name.Contains('.', StringComparison.Ordinal)
            ? byFullName
            : types.Where(t => Matches(t, t.Name)).ToList();
        if (arity is null && found.Exists(t => Arity(t) == 0))
        {
            // A plain name stands for the non-generic type when there is one; a generic namesake needs its parameters.
            found = found.FindAll(t => Arity(t) == 0);
        }

        if (found.Count > 1)
        {
            var written = arity is null ? name : $"{name} of arity {arity}";
            if (!ambiguous.Contains(written, StringComparer.Ordinal))
            {
                ambiguous.Add(written);
            }

            return null;
        }

        return found.Count == 1 ? found[0] : null;
    }

    private static int Arity(Type type)
    {
        var tick = type.Name.IndexOf('`', StringComparison.Ordinal);
        return tick < 0 ? 0 : int.Parse(type.Name[(tick + 1)..], System.Globalization.CultureInfo.InvariantCulture);
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

    private sealed record Row(string Area, string Minos, string Upstream, string Fit, List<string> MinosNames, List<string> UpstreamNames);

    private static List<Row> ReadRows()
    {
        var path = Path.Combine(PublishedPages.Root, "docs", Page);
        Assert.True(File.Exists(path), $"The mapping page docs/{Page} exists.");

        var lines = File.ReadAllLines(path);
        var rows = new List<Row>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        string? area = null;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].StartsWith("## ", StringComparison.Ordinal))
            {
                area = lines[i][3..].Trim();
                continue;
            }

            if (!lines[i].StartsWith('|'))
            {
                continue;
            }

            var start = i;
            while (i + 1 < lines.Length && lines[i + 1].StartsWith('|'))
            {
                i++;
            }

            var where = $"{Page}, line {start + 1}";
            Assert.True(area is not null && Array.IndexOf(AreaHeadings, area) >= 0, $"{where}: a table sits under one of the area headings, not under '{area}'.");
            Assert.True(tables.Add(area!), $"{where}: '{area}' has exactly one table, but a second one starts here.");
            Assert.True(i - start >= 1, $"{where}: the table has a header row and a separator row.");
            Assert.True(
                Cells(lines[start]).SequenceEqual(Header, StringComparer.Ordinal),
                $"{where}: the header is '| {string.Join(" | ", Header)} |'.");
            var separator = Cells(lines[start + 1]);
            Assert.True(separator.Length == Header.Length && Array.TrueForAll(separator, c => SeparatorCell().IsMatch(c)), $"{where}: the second line is a separator row of {Header.Length} cells.");

            for (var r = start + 2; r <= i; r++)
            {
                var cells = Cells(lines[r]);
                Assert.True(cells.Length == Header.Length, $"{Page}, line {r + 1}: the row has {Header.Length} cells, not {cells.Length}.");
                rows.Add(new Row(area!, cells[0], cells[1], cells[2], Names(cells[0], "Minos", r + 1), Names(cells[1], "Upstream", r + 1)));
            }
        }

        return rows;
    }

    private static string[] Cells(string line)
    {
        var trimmed = line.Trim();
        Assert.True(trimmed.Length > 1 && trimmed[0] == '|' && trimmed[^1] == '|', $"{Page}: the table line '{line}' starts and ends with '|'.");
        return [.. trimmed[1..^1].Split('|').Select(c => c.Trim())];
    }

    /// <summary>The identifiers of a cell. An empty cell or an em dash has none; any other text must be code spans with this side's prefix.</summary>
    private static List<string> Names(string cell, string side, int line)
    {
        var names = new List<string>();
        if (cell.Length == 0 || string.Equals(cell, "\u2014", StringComparison.Ordinal))
        {
            return names;
        }

        var rest = cell;
        foreach (Match m in PrefixedSpan().Matches(cell))
        {
            Assert.True(string.Equals(m.Groups["side"].Value, side, StringComparison.Ordinal), $"{Page}, line {line}: the {side} cell '{cell}' has only '{side}: ' code spans.");
            names.Add(m.Groups["name"].Value.Trim());
            rest = rest.Replace(m.Value, string.Empty, StringComparison.Ordinal);
        }

        Assert.True(names.Count > 0 && rest.Trim(' ', ',').Length == 0, $"{Page}, line {line}: the {side} cell '{cell}' is empty, an em dash, or code spans starting '{side}: '.");
        return names;
    }

    [GeneratedRegex("^:?-{3,}:?$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SeparatorCell();

    [GeneratedRegex("`(?<side>Minos|Upstream): (?<name>[^`]+)`", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PrefixedSpan();
}
