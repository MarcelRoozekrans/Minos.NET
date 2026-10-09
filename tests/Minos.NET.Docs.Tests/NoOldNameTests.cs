using System.Text;
using System.Text.RegularExpressions;

namespace Minos.Docs.Tests;

// The rename to Minos (Phase 6.1) must leave no trace of the old name outside history and measured data: not
// ZeroAlloc.Jev in any spelling, not a JEV analyzer ID, and not a word built on jev in any case. The bare word Jev
// stays where it names TypeSafe's model; every other allowed use is listed below with the reason it may stay.
public sealed partial class NoOldNameTests
{
    // History and measured data, read whole.
    private static readonly string[] ExcludedPrefixes =
    [
        "CHANGELOG.md", "docs/planning/", "docs/plans/", "docs/superpowers/", "benchmarks/compare/results/", "tools/rename/",
    ];

    // Shipped analyzer releases and public API records, and npm's lock.
    private static readonly string[] ExcludedNames =
    [
        "AnalyzerReleases.Shipped.md", "PublicAPI.Shipped.txt", "package-lock.json",
    ];

    // The unshipped public API record is read, but its *REMOVED* lines name the API that shipped under the old name and
    // are skipped. Every line it adds must use the new name.
    private const string UnshippedApi = "PublicAPI.Unshipped.txt";
    private const string RemovedApiEntry = "*REMOVED*";

    // Every tracked file is read as text except these binary types.
    private static readonly string[] BinaryExtensions =
    [
        ".png", ".ico", ".jpg", ".jpeg", ".gif", ".webp", ".woff", ".woff2", ".ttf", ".otf", ".nupkg", ".snupkg", ".snk",
        ".dll", ".exe", ".pdb", ".zip", ".pdf",
    ];

    // This test names every old form it looks for.
    private const string Self = "tests/Minos.NET.Docs.Tests/NoOldNameTests.cs";

    // Spans that may name the old forms, each scoped to a path: "" is every file, a path ending in / is a folder, and
    // anything else is one file. A section, when given, is the Markdown heading the line must sit under; a generated
    // allowance holds only between a page's generated-table markers.
    private static readonly Allowance[] Allowances =
    [
        // Third-party clients the benchmark measures: identifiers built on JevSharp or JevNet, and the model alias JevLatest.
        new("", null, ThirdPartyOrAliasIdentifier()),

        // Third-party client names and ids: the Jev.Net package, and the jev-net and jevsharp benchmark ids.
        new("", null, ThirdPartyName()),

        // TypeSafe model ids: jev-latest, jev-preview and versioned ids such as jev-1.13.0, and the fake ids tests send.
        new("", null, ModelId()),

        // The live alias test recognises a versioned model id by its "jev-" prefix.
        new("tests/Minos.NET.Live.Tests/AliasLiveTests.cs", null, ModelIdPrefix()),

        // The jev NuGet tag, so the package is found by the model's name.
        new("Directory.Build.props", null, PackageTag()),
        new("tests/Minos.NET.PackTests/PackageContentTests.cs", null, NuspecTag()),

        // The JEV rules shipped before the rename; the test that reads that history names them.
        new("tests/Minos.NET.Docs.Tests/DiagnosticsTests.cs", null, ShippedRuleHistory()),

        // The unshipped release records the removal of the rules that shipped as JEV under ZeroAlloc.Jev.
        new("src/Minos.NET.Analyzers/AnalyzerReleases.Unshipped.md", "### Removed Rules", RemovedRuleRow()),

        // The published benchmark results were measured before the rename: code that reads them keeps their labels.
        new("benchmarks/", null, PublishedResultLabel()),
        new("benchmarks/Minos.NET.Benchmarks.Compare/Adapters/ClientAdapters.cs", null, PublishedClientIdInDocs()),
        new("benchmarks/compare/README.md", null, PublishedProjectName()),
        new("tests/Minos.NET.Docs.Tests/ComparisonTests.cs", null, PublishedResultLabel()),

        // JevSharp's own types, which its adapter has to name.
        new("benchmarks/Minos.NET.Benchmarks.Compare/Adapters/JevSharpAdapter.cs", null, JevSharpType()),

        // The performance page shows published runs: inside the generated tables, their headings, client rows, library
        // column, order line and run links; outside them, only the sentence that says the runs carry the old name.
        new("docs/performance.md", null, PublishedRunTable(), Generated: true),
        new("docs/performance.md", null, PublishedRunSentence()),

        // The logo's design record quotes the brief as it was given, during the rename.
        new("docs/design/LOGO.md", null, LogoBriefRename()),
    ];

    [Fact]
    public void NoTrackedFile_StillNamesTheOldName()
    {
        var offenders = new List<string>();
        foreach (var path in TrackedTextFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, path), Encoding.UTF8);
            var section = "";
            var generated = false;
            var unshippedApi = path.EndsWith(UnshippedApi, StringComparison.Ordinal);
            for (var i = 0; i < lines.Length; i++)
            {
                if (unshippedApi && lines[i].StartsWith(RemovedApiEntry, StringComparison.Ordinal))
                {
                    continue;
                }

                if (lines[i].StartsWith('#'))
                {
                    section = lines[i].Trim();
                }

                if (GeneratedStart().IsMatch(lines[i]))
                {
                    generated = true;
                }
                else if (lines[i].StartsWith("<!-- end", StringComparison.Ordinal))
                {
                    generated = false;
                }

                if (IsOffending(path, section, generated, lines[i]))
                {
                    offenders.Add($"{path}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "The old name is still here:\n" + string.Join('\n', offenders));
    }

    private static bool IsOffending(string path, string section, bool generated, string line)
    {
        var rest = line;
        foreach (var allowance in Allowances)
        {
            if (allowance.Applies(path, section, generated))
            {
                rest = allowance.Span.Replace(rest, " ");
            }
        }

        if (OldName().IsMatch(rest))
        {
            return true;
        }

        // Any other word built on jev, such as JevClient, jevClient or ScriptedJev. Only the bare words Jev and Jevs,
        // in that case, are prose about the model.
        foreach (Match word in JevWord().Matches(rest))
        {
            if (word.Value is not ("Jev" or "Jevs"))
            {
                return true;
            }
        }

        return false;
    }

    private static string[] TrackedTextFiles()
    {
        // -z gives each path verbatim, NUL-separated, so a path git would quote is still read.
        var start = new System.Diagnostics.ProcessStartInfo("git", "ls-files -z")
        {
            WorkingDirectory = PublishedPages.Root,
            RedirectStandardOutput = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        using var git = System.Diagnostics.Process.Start(start)!;
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        var files = new List<string>();
        foreach (var path in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!IsBinary(path) && !IsExcluded(path))
            {
                files.Add(path);
            }
        }

        Assert.NotEmpty(files);
        return [.. files];
    }

    private static bool IsBinary(string path)
    {
        foreach (var extension in BinaryExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsExcluded(string path)
    {
        if (path.Equals(Self, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var prefix in ExcludedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var name in ExcludedNames)
        {
            if (path.EndsWith(name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed record Allowance(string Path, string? Section, Regex Span, bool Generated = false)
    {
        public bool Applies(string path, string section, bool generated)
        {
            var inPath = Path.Length == 0
                || (Path.EndsWith('/') ? path.StartsWith(Path, StringComparison.Ordinal) : path.Equals(Path, StringComparison.Ordinal));
            return inPath
                && (Section is null || section.Equals(Section, StringComparison.Ordinal))
                && (!Generated || generated);
        }
    }

    // ZeroAlloc.Jev or zeroalloc-jev (and so the old repository), the old site, or a JEV analyzer ID, in any case.
    [GeneratedRegex(@"ZeroAlloc[.-]Jev|jev\.zeroalloc\.net|\bJEV\d{3}\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OldName();

    [GeneratedRegex(@"\b\w*jev\w*\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JevWord();

    // A whole word, and never one that follows the old name's ZeroAlloc. or zeroalloc- prefix.
    [GeneratedRegex(@"(?<!\w)(?<!(?i:zeroalloc)[.-])\w*(?:JevSharp|JevNet|JevLatest)\w*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThirdPartyOrAliasIdentifier();

    [GeneratedRegex(@"(?<![\w.-])(?:Jev\.Net|jev-net|jevsharp)(?![\w-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThirdPartyName();

    [GeneratedRegex(@"(?<![\w.-])jev-(?:latest|preview|\d[\w.-]*|test-model|typesafe|openrouter|first|second)(?![\w-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModelId();

    [GeneratedRegex(@"""jev-""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModelIdPrefix();

    [GeneratedRegex(@"(?<=<PackageTags>[\w;-]*)(?<=[>;])jev(?=;)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PackageTag();

    [GeneratedRegex(@"(?<=PackageTags = ""[\w -]*)(?<= )jev(?= )", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NuspecTag();

    [GeneratedRegex(@"\bJev108AndJev109_\w+|\bJEV(?:10[89]\b|\\d\+|(?= ids\b))|ZeroAlloc\\\.Jev", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ShippedRuleHistory();

    [GeneratedRegex(@"^JEV\d{3} \| ZeroAlloc\.Jev \| \w+ \| \w+\s*$", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex RemovedRuleRow();

    [GeneratedRegex(@"""(?:ZeroAlloc\.Jev|zeroalloc-jev)""", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PublishedResultLabel();

    [GeneratedRegex(@"<c>zeroalloc-jev</c>", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PublishedClientIdInDocs();

    [GeneratedRegex(@"(?<=under the name |--project )ZeroAlloc\.Jev\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PublishedProjectName();

    [GeneratedRegex(@"\bJev(?:Request|Protocol|Value|Question|Client|ClientOptions)\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JevSharpType();

    // A generated table's heading, its client row and that row's library column, its order line, and its run links.
    [GeneratedRegex(
        @"^### ZeroAlloc\.Jev: (?:client comparison|across runs)$"
        + @"|(?<=^\| \*\*)zeroalloc-jev(?=\*\* \|)"
        + @"|(?<=^\| \*\*zeroalloc-jev\*\* \| )ZeroAlloc\.Jev(?= \S+ \|)"
        + @"|(?<=^Order: .*; throughput .*)\bzeroalloc-jev(?=[,.])"
        + @"|https://github\.com/ZeroAlloc-Net/ZeroAlloc\.Jev/actions/runs/\d+",
        RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PublishedRunTable();

    // The one sentence that says the published runs were measured under the old name.
    [GeneratedRegex(@"(?<=so their result files call it )ZeroAlloc\.Jev(?=;)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PublishedRunSentence();

    [GeneratedRegex(@"^<!-- (?:comparison|acrossRuns): ", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex GeneratedStart();

    [GeneratedRegex(@"being renamed from ZeroAlloc\.Jev\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogoBriefRename();
}
