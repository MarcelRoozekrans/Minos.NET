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

    // Shipped analyzer releases and public API records, the removed entries of the unshipped API record, and npm's lock.
    private static readonly string[] ExcludedNames =
    [
        "AnalyzerReleases.Shipped.md", "PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt", "package-lock.json",
    ];

    // This test names every old form it looks for.
    private const string Self = "tests/Minos.NET.Docs.Tests/NoOldNameTests.cs";

    // Spans that may name the old forms, each scoped to a path: "" is every file, a path ending in / is a folder, and
    // anything else is one file. A section, when given, is the Markdown heading the line must sit under.
    private static readonly Allowance[] Allowances =
    [
        // Third-party clients the benchmark measures: identifiers built on JevSharp or JevNet, and the model alias JevLatest.
        new("", null, ThirdPartyOrAliasIdentifier()),

        // Third-party client names and ids: the Jev.Net package, and the jev-net and jevsharp benchmark ids.
        new("", null, ThirdPartyName()),

        // TypeSafe model ids, such as jev-latest, jev-preview and jev-1.13.0, the fake ids tests send in that form, and
        // the quoted "jev-" prefix code uses to recognise a versioned id.
        new("", null, ModelId()),

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
        new("tests/Minos.NET.Benchmarks.Tests/", null, PublishedResultLabel()),

        // JevSharp's own types, which its adapter has to name.
        new("benchmarks/Minos.NET.Benchmarks.Compare/Adapters/JevSharpAdapter.cs", null, JevSharpType()),

        // The performance page shows published runs: their tables, their headings and their run links.
        new("docs/performance.md", null, PerformancePageLabel()),

        // The logo's design record quotes the brief as it was given, during the rename.
        new("docs/design/LOGO.md", null, LogoBriefRename()),
    ];

    [Fact]
    public void NoTrackedFile_StillNamesTheOldName()
    {
        var offenders = new List<string>();
        foreach (var path in TrackedTextFiles())
        {
            var lines = File.ReadAllLines(Path.Combine(PublishedPages.Root, path));
            var section = "";
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].StartsWith('#'))
                {
                    section = lines[i].Trim();
                }

                if (IsOffending(path, section, lines[i]))
                {
                    offenders.Add($"{path}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "The old name is still here:\n" + string.Join('\n', offenders));
    }

    private static bool IsOffending(string path, string section, string line)
    {
        var rest = line;
        foreach (var allowance in Allowances)
        {
            if (allowance.Applies(path, section))
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
        var start = new System.Diagnostics.ProcessStartInfo("git", "ls-files")
        {
            WorkingDirectory = PublishedPages.Root,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var git = System.Diagnostics.Process.Start(start)!;
        var output = git.StandardOutput.ReadToEnd();
        git.WaitForExit();
        var files = new List<string>();
        foreach (var path in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (TextExtension().IsMatch(path) && !IsExcluded(path))
            {
                files.Add(path);
            }
        }

        Assert.NotEmpty(files);
        return [.. files];
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

    private sealed record Allowance(string Path, string? Section, Regex Span)
    {
        public bool Applies(string path, string section)
        {
            var inPath = Path.Length == 0
                || (Path.EndsWith('/') ? path.StartsWith(Path, StringComparison.Ordinal) : path.Equals(Path, StringComparison.Ordinal));
            return inPath && (Section is null || section.Equals(Section, StringComparison.Ordinal));
        }
    }

    // ZeroAlloc.Jev or zeroalloc-jev (and so the old repository), the old site, or a JEV analyzer ID, in any case.
    [GeneratedRegex(@"ZeroAlloc[.-]Jev|jev\.zeroalloc\.net|\bJEV\d{3}\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex OldName();

    [GeneratedRegex(@"\b\w*jev\w*\b", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 1000)]
    private static partial Regex JevWord();

    [GeneratedRegex(@"\w*(?:JevSharp|JevNet|JevLatest)\w*", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThirdPartyOrAliasIdentifier();

    [GeneratedRegex(@"(?<![\w.-])(?:Jev\.Net|jev-net|jevsharp)(?![\w-])", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ThirdPartyName();

    [GeneratedRegex(@"(?<![\w.-])jev-(?:[a-z0-9][a-z0-9.-]*|(?=""))", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex ModelId();

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

    [GeneratedRegex(@"https://github\.com/ZeroAlloc-Net/ZeroAlloc\.Jev/actions/runs/\d+|ZeroAlloc\.Jev|zeroalloc-jev", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PerformancePageLabel();

    [GeneratedRegex(@"being renamed from ZeroAlloc\.Jev\b", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LogoBriefRename();

    [GeneratedRegex(@"\.(cs|csproj|props|targets|slnx|md|json|yml|yaml|ps1|sh|py|ts|txt|editorconfig)$",
        RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex TextExtension();
}
