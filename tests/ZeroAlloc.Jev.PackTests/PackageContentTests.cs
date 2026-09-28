using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;

namespace ZeroAlloc.Jev.PackTests;

/// <summary>
/// Packs the shipping <c>ZeroAlloc.Jev</c> project once, into a unique temp directory, via <c>dotnet pack</c>
/// run as a child <see cref="Process"/>. Reuses the src output the solution build already produced with
/// <c>--no-build</c> when it exists, and redirects the generated nuspec away from the project's own
/// <c>obj</c> directory, so this never races or corrupts the concurrent solution build or a later pack step.
/// </summary>
public sealed class PackFixture : IDisposable
{
    public string NupkgPath { get; }

    public string NuspecPath { get; }

    private readonly string _tempDir;

    public PackFixture()
    {
        var repoRoot = FindRepoRoot();
        var configuration = typeof(PackFixture).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? "Release";

        _tempDir = Path.Combine(Path.GetTempPath(), "jev-pack-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var projectPath = Path.Combine(repoRoot, "src", "ZeroAlloc.Jev", "ZeroAlloc.Jev.csproj");
        var srcOutputDll = Path.Combine(repoRoot, "src", "ZeroAlloc.Jev", "bin", configuration, "net10.0", "ZeroAlloc.Jev.dll");
        var reuseSrcOutput = File.Exists(srcOutputDll);

        // Redirect the generated .nuspec into our own temp dir: dotnet pack --no-build still writes it under
        // the project's obj/, and this test can otherwise race a concurrent solution build or pack step there.
        var nuspecOutputPath = Path.Combine(_tempDir, "nuspec-obj") + Path.DirectorySeparatorChar;

        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot,
        };
        startInfo.ArgumentList.Add("pack");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add(configuration);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(_tempDir);
        startInfo.ArgumentList.Add($"-p:NuspecOutputPath={nuspecOutputPath}");
        if (reuseSrcOutput)
        {
            startInfo.ArgumentList.Add("--no-build");
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start 'dotnet pack'.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildPackFailureMessage(process.ExitCode, stdout, stderr));
        }

        var nupkg = Directory.GetFiles(_tempDir, "*.nupkg").FirstOrDefault();
        if (nupkg is null)
        {
            throw new InvalidOperationException(
                $"'dotnet pack' exited 0 but produced no .nupkg in '{_tempDir}'.{Environment.NewLine}" +
                BuildPackFailureMessage(process.ExitCode, stdout, stderr));
        }

        NupkgPath = nupkg;
        NuspecPath = ExtractNuspec(nupkg, _tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle on Windows must not fail the test run.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a lingering handle on Windows must not fail the test run.
        }
    }

    private static string BuildPackFailureMessage(int exitCode, string stdout, string stderr)
        => $"'dotnet pack' exited with code {exitCode}.{Environment.NewLine}" +
           $"--- stdout ---{Environment.NewLine}{stdout}{Environment.NewLine}" +
           $"--- stderr ---{Environment.NewLine}{stderr}";

    private static string ExtractNuspec(string nupkgPath, string tempDir)
    {
        using var archive = ZipFile.OpenRead(nupkgPath);
        var entry = archive.Entries.First(e => e.FullName.EndsWith(".nuspec", StringComparison.Ordinal));
        var destination = Path.Combine(tempDir, entry.Name);
        entry.ExtractToFile(destination, overwrite: true);
        return destination;
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ZeroAlloc.Jev.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find 'ZeroAlloc.Jev.slnx' by walking up from '{AppContext.BaseDirectory}'.");
    }
}

/// <summary>Inspects the packed <c>ZeroAlloc.Jev.nupkg</c> built once by <see cref="PackFixture"/>.</summary>
public sealed class PackageContentTests : IClassFixture<PackFixture>
{
    private static readonly string[] ExpectedRuntimeDependencyIds =
    [
        "ZeroAlloc.Rest",
        "ZeroAlloc.Rest.SystemTextJson",
        "ZeroAlloc.Results",
        "ZeroAlloc.Resilience",
    ];

    private readonly PackFixture _fixture;

    public PackageContentTests(PackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsGeneratorAsAnalyzer()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);

        Assert.Contains(
            archive.Entries,
            e => string.Equals(e.FullName, "analyzers/dotnet/cs/ZeroAlloc.Jev.Generator.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipsAnalyzerAndCodeFixesAsAnalyzers()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);

        Assert.Contains(
            archive.Entries,
            e => string.Equals(e.FullName, "analyzers/dotnet/cs/ZeroAlloc.Jev.Analyzers.dll", StringComparison.Ordinal));
        Assert.Contains(
            archive.Entries,
            e => string.Equals(e.FullName, "analyzers/dotnet/cs/ZeroAlloc.Jev.CodeFixes.dll", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipsLibraryAndDocs()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);

        Assert.Contains(
            archive.Entries,
            e => string.Equals(e.FullName, "lib/net10.0/ZeroAlloc.Jev.dll", StringComparison.Ordinal));
        Assert.Contains(
            archive.Entries,
            e => string.Equals(e.FullName, "lib/net10.0/ZeroAlloc.Jev.xml", StringComparison.Ordinal));
    }

    [Fact]
    public void ShipsNothingElseExecutable()
    {
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);
        string[] allowed =
        [
            "lib/net10.0/ZeroAlloc.Jev.dll",
            "analyzers/dotnet/cs/ZeroAlloc.Jev.Generator.dll",
            "analyzers/dotnet/cs/ZeroAlloc.Jev.Analyzers.dll",
            "analyzers/dotnet/cs/ZeroAlloc.Jev.CodeFixes.dll",
        ];

        var dllEntries = archive.Entries
            .Select(e => e.FullName)
            .Where(name => name.EndsWith(".dll", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(dllEntries);
        Assert.All(dllEntries, entry => Assert.Contains(entry, allowed));
    }

    [Fact]
    public void DependsOnRuntimePackagesOnly()
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var nuspec = XDocument.Load(_fixture.NuspecPath);

        var group = nuspec.Root!
            .Element(ns + "metadata")!
            .Element(ns + "dependencies")!
            .Elements(ns + "group")
            .First(g => string.Equals((string?)g.Attribute("targetFramework"), "net10.0", StringComparison.Ordinal));

        var dependencyIds = group.Elements(ns + "dependency")
            .Select(d => (string)d.Attribute("id")!)
            .ToArray();

        Assert.Equal(
            ExpectedRuntimeDependencyIds.OrderBy(id => id, StringComparer.Ordinal),
            dependencyIds.OrderBy(id => id, StringComparer.Ordinal));
        Assert.All(dependencyIds, id =>
        {
            Assert.DoesNotContain("Generator", id, StringComparison.Ordinal);
            Assert.DoesNotContain("Analyzers", id, StringComparison.Ordinal);
            Assert.DoesNotContain("CodeFixes", id, StringComparison.Ordinal);
            Assert.DoesNotContain("CodeAnalysis", id, StringComparison.Ordinal);
        });
    }
}
