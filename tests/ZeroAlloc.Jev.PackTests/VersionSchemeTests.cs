using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ZeroAlloc.Jev.PackTests;

/// <summary>
/// The version Directory.Build.props gives a build, for a manifest it is pointed at: a build that is not a release is a
/// prerelease that sorts below the manifest's version, and -p:JevRelease=true gives that version exactly, in the
/// assembly's informational version as well as the package's.
/// </summary>
public sealed class VersionSchemeTests : IDisposable
{
    private readonly string _manifest = Path.Combine(Path.GetTempPath(), "jev-manifest-" + Guid.NewGuid().ToString("N") + ".json");

    [Theory]
    [InlineData("0.4.0", "0.4.0-local", "0.4.0.0")]
    [InlineData("1.0.0-beta.1", "1.0.0-0.local.beta.1", "1.0.0.0")]
    [InlineData("2.3.4-rc.2.x-y", "2.3.4-0.local.rc.2.x-y", "2.3.4.0")]
    public void A_build_that_is_not_a_release_is_a_prerelease_below_the_manifests_version(string manifest, string expected, string numeric)
    {
        var properties = Evaluate(manifest, release: false);

        Assert.Equal(expected, properties["Version"]);
        Assert.Equal(expected, properties["PackageVersion"]);
        Assert.Equal(expected, properties["InformationalVersion"]);
        Assert.Equal(numeric, properties["AssemblyVersion"]);
        Assert.Equal(numeric, properties["FileVersion"]);
        Assert.True(SemVer.Compare(expected, manifest) < 0, $"{expected} sorts below {manifest}.");
    }

    [Theory]
    [InlineData("0.4.0", "0.4.0.0")]
    [InlineData("1.0.0-beta.1", "1.0.0.0")]
    public void A_release_build_is_the_manifests_version_in_the_package_and_the_assembly(string manifest, string numeric)
    {
        var properties = Evaluate(manifest, release: true);

        Assert.Equal(manifest, properties["Version"]);
        Assert.Equal(manifest, properties["PackageVersion"]);
        Assert.Equal(manifest, properties["InformationalVersion"]);
        Assert.Equal(numeric, properties["AssemblyVersion"]);
        Assert.Equal(numeric, properties["FileVersion"]);
    }

    [Fact]
    public void A_local_prerelease_sorts_below_every_prerelease_of_its_version_and_above_the_last_release()
    {
        Assert.True(SemVer.Compare("1.0.0-0.local.beta.1", "1.0.0-alpha") < 0);
        Assert.True(SemVer.Compare("1.0.0-0.local.beta.1", "1.0.0-beta.1") < 0);
        Assert.True(SemVer.Compare("1.0.0-0.local.beta.1", "0.9.9") > 0);
        Assert.True(SemVer.Compare("1.0.0-beta.1.local", "1.0.0-beta.1") > 0, "Appending to the prerelease would sort above it.");
        Assert.True(SemVer.Compare("0.4.0-local", "0.4.0") < 0);
    }

    public void Dispose() => File.Delete(_manifest);

    // Evaluates the library project against a manifest holding the version, through GetAssemblyVersion, the SDK target
    // that sets the assembly attributes' values. It writes nothing.
    private Dictionary<string, string> Evaluate(string version, bool release)
    {
        File.WriteAllText(_manifest, "{ \".\": \"" + version + "\" }");
        var root = FindRepoRoot();
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = root,
        };
        string[] arguments =
        [
            "msbuild", Path.Combine(root, "src", "ZeroAlloc.Jev", "ZeroAlloc.Jev.csproj"), "-nologo",
            "-t:GetAssemblyVersion", $"-p:JevReleaseManifest={_manifest}", $"-p:JevRelease={(release ? "true" : "false")}",
            "-getProperty:Version", "-getProperty:PackageVersion", "-getProperty:InformationalVersion",
            "-getProperty:AssemblyVersion", "-getProperty:FileVersion",
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start 'dotnet msbuild'.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        Assert.True(process.WaitForExit(TimeSpan.FromMinutes(2)), "dotnet msbuild finished within two minutes.");
        Assert.True(process.ExitCode == 0, $"dotnet msbuild exited with {process.ExitCode}: {output} {error.Result}");

        using var json = JsonDocument.Parse(output);
        return json.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Jev.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root.");
    }

    // SemVer 2 precedence, without build metadata, as NuGet orders versions apart from its case-insensitivity.
    private static class SemVer
    {
        public static int Compare(string left, string right)
        {
            var (leftCore, leftPre) = Split(left);
            var (rightCore, rightPre) = Split(right);
            var core = leftCore.CompareTo(rightCore);
            if (core != 0)
            {
                return core;
            }

            if (leftPre.Length == 0 || rightPre.Length == 0)
            {
                // A version with no prerelease sorts above the same version with one.
                return rightPre.Length.CompareTo(leftPre.Length);
            }

            for (var i = 0; i < Math.Min(leftPre.Length, rightPre.Length); i++)
            {
                var order = CompareIdentifier(leftPre[i], rightPre[i]);
                if (order != 0)
                {
                    return order;
                }
            }

            return leftPre.Length.CompareTo(rightPre.Length);
        }

        private static (Version Core, string[] Prerelease) Split(string version)
        {
            var dash = version.IndexOf('-', StringComparison.Ordinal);
            var core = Version.Parse(dash < 0 ? version : version[..dash]);
            return (core, dash < 0 ? [] : version[(dash + 1)..].Split('.'));
        }

        // Numeric identifiers compare as numbers and sort below alphanumeric ones, which compare as text.
        private static int CompareIdentifier(string left, string right)
        {
            var leftNumeric = long.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = long.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            return (leftNumeric, rightNumeric) switch
            {
                (true, true) => leftNumber.CompareTo(rightNumber),
                (true, false) => -1,
                (false, true) => 1,
                _ => string.CompareOrdinal(left, right),
            };
        }
    }
}
