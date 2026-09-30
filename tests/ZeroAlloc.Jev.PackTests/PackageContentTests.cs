using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
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

    /// <summary>Gets the version this fixture packs: unique per run, so no package cache can hold an older build of it.</summary>
    public string PackageVersion { get; } = "0.0.0-packtest.g" + Guid.NewGuid().ToString("N");

    /// <summary>Gets the directory holding the packed <c>.nupkg</c>, usable as a local package source.</summary>
    public string OutputDirectory => _tempDir;

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
        startInfo.ArgumentList.Add($"-p:PackageVersion={PackageVersion}");
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
        "ZeroAlloc.Validation",
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

    [Fact]
    public void ValidationDependency_ExcludesBuildAndAnalyzers()
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var nuspec = XDocument.Load(_fixture.NuspecPath);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var dependency = Assert.Single(
            nuspec.Descendants(ns + "dependency"),
            d => string.Equals((string?)d.Attribute("id"), "ZeroAlloc.Validation", StringComparison.Ordinal));
#pragma warning restore HLQ005
        var exclude = (string?)dependency.Attribute("exclude");
        var include = (string?)dependency.Attribute("include");

        // NuGet writes PrivateAssets as an exclude list or as the complementary include list.
        if (exclude is not null)
        {
            var excluded = exclude.Split(',', StringSplitOptions.TrimEntries);
            Assert.True(
                excluded.Contains("Build", StringComparer.OrdinalIgnoreCase) && excluded.Contains("Analyzers", StringComparer.OrdinalIgnoreCase),
                $"Found the exclude form '{exclude}', which must list Build and Analyzers.");
        }
        else
        {
            Assert.True(include is not null, "Found neither an exclude nor an include attribute on the ZeroAlloc.Validation dependency.");
            var included = include.Split(',', StringSplitOptions.TrimEntries);
            Assert.True(
                !included.Contains("Build", StringComparer.OrdinalIgnoreCase) && !included.Contains("Analyzers", StringComparer.OrdinalIgnoreCase),
                $"Found the include form '{include}', which must contain neither Build nor Analyzers.");
        }
    }

    // NuGet flows a package's analyzers to consumers transitively whatever PrivateAssets says, NuGet/Home#6720, so the
    // ZeroAlloc.Validation and ZeroAlloc.Pipeline analyzers do reach the consumer. This asserts they stay inert: nothing
    // is emitted and the build stays clean, rather than that they are absent.
    [Fact]
    public void PackedConsumer_WithoutValidateTypes_BuildsCleanAndGetsNoValidationSources()
    {
        var consumer = Path.Combine(_fixture.OutputDirectory, "consumer");
        Directory.CreateDirectory(consumer);
        var machinePackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

        // A private packages folder, with the machine's cache as a read-only fallback for the other dependencies: the
        // freshly packed, uniquely versioned ZeroAlloc.Jev can only come from the pack output.
        File.WriteAllText(Path.Combine(consumer, "nuget.config"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <config>
                <add key="globalPackagesFolder" value="{Path.Combine(consumer, "packages")}" />
              </config>
              <packageSources>
                <clear />
                <add key="packed" value="{_fixture.OutputDirectory}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
              <fallbackPackageFolders>
                <add key="machine" value="{machinePackages}" />
              </fallbackPackageFolders>
            </configuration>
            """);
        File.WriteAllText(Path.Combine(consumer, "Consumer.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="ZeroAlloc.Jev" Version="{_fixture.PackageVersion}" />
              </ItemGroup>
            </Project>
            """);
        // A minimal question set, so ZeroAlloc.Jev's own generator has something to emit: the positive control for
        // the directory check below. It has no [Validate] type, so the Validation and Pipeline generators have none.
        File.WriteAllText(Path.Combine(consumer, "Questions.cs"), """
            using ZeroAlloc.Jev;

            namespace Consumer;

            [JevQuestions]
            public partial record UrgencyCheck
            {
                [Noul("Does this convey urgency?")]
                public partial Noul IsUrgent { get; }
            }
            """);

        // Non-vacuity guard: the check must see package analyzers, so ZeroAlloc.Jev's own generator has to be listed.
        // Restore separately, so the msbuild output holds nothing but the JSON.
        RunDotnet(consumer, "restore", "Consumer.csproj");
        var listing = RunDotnet(consumer, "msbuild", "Consumer.csproj", "-t:ResolveLockFileAnalyzers", "-getItem:Analyzer");

        using var output = JsonDocument.Parse(listing);
        var analyzers = output.RootElement.GetProperty("Items").GetProperty("Analyzer").EnumerateArray()
            .Select(item => Path.GetFileName(item.GetProperty("Identity").GetString()!))
            .ToArray();

        Assert.Contains("ZeroAlloc.Jev.Generator.dll", analyzers);

        // It must build with no warning, and no source may come from the Validation or Pipeline generators.
        RunDotnet(consumer, "build", "Consumer.csproj", "-c", "Release", "-p:EmitCompilerGeneratedFiles=true", "-warnaserror");

        // Each generated file sits in obj/.../generated/{generator assembly}/{generator}/: name its generator assembly.
        var generators = Directory.GetFiles(Path.Combine(consumer, "obj"), "*.cs", SearchOption.AllDirectories)
            .Where(file => file.Contains($"{Path.DirectorySeparatorChar}generated{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file))!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.Contains("ZeroAlloc.Jev.Generator", generators);
        Assert.DoesNotContain(generators, name => name.StartsWith("ZeroAlloc.Validation", StringComparison.Ordinal));
        Assert.DoesNotContain(generators, name => name.StartsWith("ZeroAlloc.Pipeline", StringComparison.Ordinal));
    }

    private static string RunDotnet(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start 'dotnet'.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return process.ExitCode == 0
            ? stdout
            : throw new InvalidOperationException(
                $"'dotnet {string.Join(' ', arguments)}' exited with code {process.ExitCode}.{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
    }
}
