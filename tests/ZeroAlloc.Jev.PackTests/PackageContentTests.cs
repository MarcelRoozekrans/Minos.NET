using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;

namespace ZeroAlloc.Jev.PackTests;

/// <summary>Packs the shipping <c>ZeroAlloc.Jev</c> project once, through <see cref="PackedProject"/>.</summary>
public sealed class PackFixture : IDisposable
{
    private readonly PackedProject _packed = new("ZeroAlloc.Jev");

    public string NupkgPath => _packed.NupkgPath;

    public string NuspecPath => _packed.NuspecPath;

    /// <summary>Gets the version this fixture packs: unique per run, so no package cache can hold an older build of it.</summary>
    public string PackageVersion => _packed.PackageVersion;

    /// <summary>Gets the directory holding the packed <c>.nupkg</c>, usable as a local package source.</summary>
    public string OutputDirectory => _packed.OutputDirectory;

    public void Dispose() => _packed.Dispose();
}

/// <summary>Inspects the packed <c>ZeroAlloc.Jev.nupkg</c> built once by <see cref="PackFixture"/>.</summary>
[Collection(Packing.Name)]
public sealed class PackageContentTests : IClassFixture<PackFixture>
{
    private static readonly string[] ExpectedRuntimeDependencyIds =
    [
        "Microsoft.Extensions.Logging.Abstractions",
        "ZeroAlloc.Rest",
        "ZeroAlloc.Rest.SystemTextJson",
        "ZeroAlloc.Results",
        "ZeroAlloc.Telemetry",
        "ZeroAlloc.Resilience",
        "ZeroAlloc.Validation",
    ];

    private readonly PackFixture _fixture;

    public PackageContentTests(PackFixture fixture) => _fixture = fixture;

    [Fact]
    public void ShipsTheIconAtTheRoot_MatchingTheFileOnDisk()
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var nuspec = XDocument.Load(_fixture.NuspecPath);
        using var archive = ZipFile.OpenRead(_fixture.NupkgPath);

        var entry = archive.GetEntry("icon.png");

        Assert.NotNull(entry);
        Assert.Equal("icon.png", (string?)nuspec.Root!.Element(ns + "metadata")!.Element(ns + "icon"));
        using var stream = entry.Open();
        using var packed = new MemoryStream();
        stream.CopyTo(packed);
        var onDisk = File.ReadAllBytes(Path.Combine(PackedProject.FindRepoRoot(), "assets", "icon.png"));
        Assert.True(onDisk.AsSpan().SequenceEqual(packed.ToArray()), "Found a packed icon.png that differs from assets/icon.png.");
    }

    // The shared ZeroAlloc icon: this is the SHA-256 of assets/icon.svg in ZeroAlloc-Net/ZeroAlloc.Rest. Every ZeroAlloc
    // package carries the same mark, so a different file here means the icon drifted from the org's.
    private const string SharedIconSvgSha256 = "94bbd8999ebb3184b5c2d1c0fd22e406b3e8e8b38eca1039cb4e326f4abd3448";

    [Fact]
    public void IconSvg_IsTheSharedZeroAllocIcon()
    {
        var svg = File.ReadAllBytes(Path.Combine(PackedProject.FindRepoRoot(), "assets", "icon.svg"));

        Assert.Equal(SharedIconSvgSha256, Convert.ToHexStringLower(SHA256.HashData(svg)));
    }

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
    public void LoggingAbstractionsDependency_IsTheFirstDotnet10Release()
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var nuspec = XDocument.Load(_fixture.NuspecPath);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var dependency = Assert.Single(
            nuspec.Descendants(ns + "dependency"),
            d => string.Equals((string?)d.Attribute("id"), "Microsoft.Extensions.Logging.Abstractions", StringComparison.Ordinal));
#pragma warning restore HLQ005

        // A floor, not a pin: 10.0.0 lets a consumer on any .NET 10 servicing release of the extensions resolve it.
        Assert.Equal("10.0.0", (string?)dependency.Attribute("version"));
    }

    [Fact]
    public void ValidationDependency_ExcludesBuildAndAnalyzers() => AssertExcludesBuildAndAnalyzers("ZeroAlloc.Validation");

    [Fact]
    public void TelemetryDependency_IsTheFloor_AndExcludesBuildAndAnalyzers()
    {
        var dependency = AssertExcludesBuildAndAnalyzers("ZeroAlloc.Telemetry");

        // 1.10.0 allows several [TraceTag] on one parameter, which the endpoint and request tags need.
        Assert.Equal("1.10.0", (string?)dependency.Attribute("version"));
    }

    // Finds the nuspec dependency on id and checks that its PrivateAssets keep Build and Analyzers out of consumers.
    private XElement AssertExcludesBuildAndAnalyzers(string id)
    {
        XNamespace ns = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd";
        var nuspec = XDocument.Load(_fixture.NuspecPath);

        // HLQ005 fires on the method name alone: this is xUnit's Assert.Single(IEnumerable), not System.Linq.Enumerable.Single().
#pragma warning disable HLQ005
        var dependency = Assert.Single(
            nuspec.Descendants(ns + "dependency"),
            d => string.Equals((string?)d.Attribute("id"), id, StringComparison.Ordinal));
#pragma warning restore HLQ005
        var exclude = (string?)dependency.Attribute("exclude");
        var include = (string?)dependency.Attribute("include");

        // NuGet writes PrivateAssets as an exclude list or as the complementary include list.
        if (exclude is not null)
        {
            var excluded = exclude.Split(',', StringSplitOptions.TrimEntries);
            Assert.True(
                excluded.Contains("Build", StringComparer.OrdinalIgnoreCase) && excluded.Contains("Analyzers", StringComparer.OrdinalIgnoreCase),
                $"Found the exclude form '{exclude}' on {id}, which must list Build and Analyzers.");
        }
        else
        {
            Assert.True(include is not null, $"Found neither an exclude nor an include attribute on the {id} dependency.");
            var included = include.Split(',', StringSplitOptions.TrimEntries);
            Assert.True(
                !included.Contains("Build", StringComparer.OrdinalIgnoreCase) && !included.Contains("Analyzers", StringComparer.OrdinalIgnoreCase),
                $"Found the include form '{include}' on {id}, which must contain neither Build nor Analyzers.");
        }

        return dependency;
    }

    // NuGet flows a package's analyzers to consumers transitively whatever PrivateAssets says, NuGet/Home#6720, so the
    // ZeroAlloc.Validation, ZeroAlloc.Pipeline and ZeroAlloc.Telemetry analyzers do reach the consumer. This asserts they stay inert: nothing
    // is emitted and the build stays clean, rather than that they are absent.
    [Fact]
    public void PackedConsumer_WithoutValidateOrInstrumentTypes_BuildsCleanAndGetsNoSourceFromThoseGenerators()
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

        // The logging constructor, through the flowed Microsoft.Extensions.Logging.Abstractions: the dependency must
        // reach the consumer's compilation, not just the nuspec.
        File.WriteAllText(Path.Combine(consumer, "Logging.cs"), """
            using Microsoft.Extensions.Logging.Abstractions;
            using ZeroAlloc.Jev;

            namespace Consumer;

            public static class Logging
            {
                public static JevClient Create() => new(new JevClientOptions { ApiKey = "consumer-key" }, NullLoggerFactory.Instance);
            }
            """);

        // The runtime attributes flow: a consumer can name them, though it needs no reference of its own to use Jev.
        File.WriteAllText(Path.Combine(consumer, "Attributes.cs"), """
            namespace Consumer;

            public static class Attributes
            {
                public static System.Type Instrument => typeof(ZeroAlloc.Telemetry.InstrumentAttribute);
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

        // Microsoft.Extensions.Logging.Abstractions ships the [LoggerMessage] generator as an analyzer. The nuspec's default
        // exclude="Build,Analyzers" does not stop it reaching this consumer, because NuGet flows a dependency's analyzers
        // transitively whatever the exclude says, NuGet/Home#6720, as for ZeroAlloc.Validation above. It must be listed
        // here, so the no-generated-source check below proves it stays inert rather than absent.
        Assert.Contains("Microsoft.Extensions.Logging.Generators.dll", analyzers);

        // ZeroAlloc.Telemetry's proxy generator reaches the consumer the same way, NuGet/Home#6720, so the check below
        // proves it stays inert rather than absent.
        Assert.Contains("ZeroAlloc.Telemetry.Generator.dll", analyzers);

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

        // With no [Instrument] interface, ZeroAlloc.Telemetry's proxy generator emits nothing.
        Assert.DoesNotContain(generators, name => name.StartsWith("ZeroAlloc.Telemetry", StringComparison.Ordinal));

        // The [LoggerMessage] generator reached the consumer, as asserted above; with no [LoggerMessage] method it emits nothing.
        Assert.DoesNotContain(generators, name => name.StartsWith("Microsoft.Extensions.Logging.Generators", StringComparison.Ordinal));
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
