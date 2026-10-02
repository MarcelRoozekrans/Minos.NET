using System.Globalization;
using System.Text.RegularExpressions;

namespace ZeroAlloc.Jev.Docs.Tests;

// The page cites gates and budgets from the AOT smoke application. These tests keep every one of them honest.
public sealed partial class NativeAotTests
{
    private const string Page = "native-aot.md";

    private static string Source(params string[] path) => File.ReadAllText(Path.Combine([PublishedPages.Root, .. path]));

    private static string AllocationChecks() => Source("samples", "ZeroAlloc.Jev.AotSmoke", "AllocationChecks.cs");

    // The literal budgets inside one gate's method: "budgetBytes: 192" and "const long BudgetBytes = 5056".
    private static int[] Budgets(string checks, string gate)
    {
        var method = Regex.Match(
            checks,
            $@"public static (?:void|async Task) {gate}\(\)(?<body>.*?)(?=\n    public static |\n    private static |\z)",
            RegexOptions.Singleline,
            TimeSpan.FromSeconds(1));
        Assert.True(method.Success, $"AllocationChecks has a gate named {gate}.");
        var budgets = new List<int>();
        foreach (Match budget in Budget().Matches(method.Groups["body"].Value))
        {
            budgets.Add(int.Parse(budget.Groups["bytes"].Value, CultureInfo.InvariantCulture));
        }

        return [.. budgets];
    }

    [Fact]
    public void TheGateTableOnThePage_IsTheSmokeAppsBudgets()
    {
        var checks = AllocationChecks();
        var rows = PageTables.Rows(Page, "The allocation budgets");

        Assert.Equal(21, rows.Length);
        Assert.Equal(rows.Length, new HashSet<string>(rows.Select(row => PageTables.Code(row[0])), StringComparer.Ordinal).Count);
        Assert.All(
            rows,
            row =>
            {
                var gate = PageTables.Code(row[0]);
                var budget = int.Parse(row[2], CultureInfo.InvariantCulture);
                Assert.True(Array.IndexOf(Budgets(checks, gate), budget) >= 0, $"{gate} has the budget {budget}.");
            });
    }

    // A gate that Main never calls would be a budget that nothing enforces.
    [Fact]
    public void EveryGateOnThePage_IsRunByTheSmokeApp()
    {
        var program = Source("samples", "ZeroAlloc.Jev.AotSmoke", "Program.cs");
        var rows = PageTables.Rows(Page, "The allocation budgets");

        Assert.All(rows, row => Assert.Contains($"AllocationChecks.{PageTables.Code(row[0])}()", program, StringComparison.Ordinal));
    }

    [Fact]
    public void ZeroBudgetGates_ReallyHaveABudgetOfZero()
    {
        var checks = AllocationChecks();

        Assert.All(["ReadNoul", "ReadChoice", "ReadScore", "JevAnswersGet", "PatternHelpers"], gate => Assert.Contains(0, Budgets(checks, gate)));
    }

    [Fact]
    public void TheAotSmokeApp_IsPublishedAsAotWithEveryWarningAnError_AndRunInCi()
    {
        var project = Source("samples", "ZeroAlloc.Jev.AotSmoke", "ZeroAlloc.Jev.AotSmoke.csproj");
        var workflow = Source(".github", "workflows", "ci.yml");

        Assert.Contains("<PublishAot>true</PublishAot>", project, StringComparison.Ordinal);
        Assert.Contains("<TreatWarningsAsErrors>true</TreatWarningsAsErrors>", project, StringComparison.Ordinal);
        Assert.Contains("aot-smoke:", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet publish samples/ZeroAlloc.Jev.AotSmoke/ZeroAlloc.Jev.AotSmoke.csproj -r linux-x64", workflow, StringComparison.Ordinal);
        Assert.Contains("./aot-out/ZeroAlloc.Jev.AotSmoke", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void BothPackages_AreAotCompatible_AndTheDependencyInjectionPackageBindsWithAGenerator()
    {
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", Source("src", "ZeroAlloc.Jev", "ZeroAlloc.Jev.csproj"), StringComparison.Ordinal);

        var injection = Source("src", "ZeroAlloc.Jev.DependencyInjection", "ZeroAlloc.Jev.DependencyInjection.csproj");
        Assert.Contains("<IsAotCompatible>true</IsAotCompatible>", injection, StringComparison.Ordinal);
        Assert.Contains("<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>", injection, StringComparison.Ordinal);
    }

    // The one reflection: the enum option set reads public fields, and says so to the trimmer.
    [Fact]
    public void TheOneReflection_IsTheEnumOptionSetReadingPublicFields_DeclaredToTheTrimmer()
    {
        var optionSet = Source("src", "ZeroAlloc.Jev", "EnumOptionSet.cs");

        Assert.Contains("DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)", optionSet, StringComparison.Ordinal);
        Assert.Contains("GetFields", optionSet, StringComparison.Ordinal);
    }

    // The page's own claim about the answer types: they are structs, which is why reading one allocates nothing.
    [Fact]
    public void TheAnswerTypes_AreStructs()
    {
        Assert.True(typeof(Noul).IsValueType);
        Assert.True(typeof(Choice<Department>).IsValueType);
        Assert.True(typeof(Score<Mood>).IsValueType);
        Assert.True(typeof(ProbabilityMap<Department>).IsValueType);
    }

    // The budgets the page quotes in prose are performance.md's.
    [Fact]
    public void TheProseFigures_ArePerformanceMds()
    {
        var performance = Source("docs", "performance.md");

        Assert.Contains("`Build` measures 6592 B", performance, StringComparison.Ordinal);
        Assert.Contains("3368 B", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.3 — DI package", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.1 — Logging", performance, StringComparison.Ordinal);
        Assert.Contains("Phase 3.2 — Telemetry", performance, StringComparison.Ordinal);
    }

    [GeneratedRegex(@"(?:budgetBytes: |BudgetBytes = )(?<bytes>\d+)", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Budget();
}
