using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace Minos.PackTests;

/// <summary>
/// Packs one shipping project under <c>src</c> once, into a unique temp directory, via <c>dotnet pack</c> run as a child
/// <see cref="Process"/>. Lets pack build the project itself, restore included, so it never packs a stale build; the build is
/// incremental, so after a solution build it rewrites nothing. It redirects the generated nuspec away from the project's own <c>obj</c> directory, so this never races or
/// corrupts the concurrent solution build or a later pack step.
/// </summary>
internal sealed class PackedProject : IDisposable
{
    private readonly string _tempDir;

    /// <param name="projectName">The project's directory and file name under <c>src</c>, such as <c>Minos.NET</c>.</param>
    /// <param name="alongside">
    /// Other projects under <c>src</c> to pack into the same directory at the same version, so a consumer of the packed
    /// project can restore its <c>Minos.NET</c> dependencies from there.
    /// </param>
    public PackedProject(string projectName, params string[] alongside)
    {
        var repoRoot = FindRepoRoot();
        var configuration = typeof(PackedProject).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
            ?? "Release";

        _tempDir = Path.Combine(Path.GetTempPath(), "minos-pack-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        foreach (var project in alongside.Prepend(projectName))
        {
            Pack(repoRoot, configuration, project);
        }

        var nupkg = Path.Combine(_tempDir, $"{projectName}.{PackageVersion}.nupkg");
        if (!File.Exists(nupkg))
        {
            throw new InvalidOperationException($"'dotnet pack' exited 0 but produced no '{Path.GetFileName(nupkg)}' in '{_tempDir}'.");
        }

        NupkgPath = nupkg;
        NuspecPath = ExtractNuspec(nupkg, _tempDir);
    }

    public string NupkgPath { get; }

    public string NuspecPath { get; }

    /// <summary>Gets the version this packs: unique per run, so no package cache can hold an older build of it.</summary>
    public string PackageVersion { get; } = "0.0.0-packtest.g" + Guid.NewGuid().ToString("N");

    /// <summary>Gets the directory holding the packed <c>.nupkg</c>, usable as a local package source.</summary>
    public string OutputDirectory => _tempDir;

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

    // Packs one project into the temp directory at PackageVersion.
    private void Pack(string repoRoot, string configuration, string projectName)
    {
        var projectPath = Path.Combine(repoRoot, "src", projectName, projectName + ".csproj");

        // Redirect the generated .nuspec into our own temp dir: dotnet pack writes it under
        // the project's obj/, and this test can otherwise race a concurrent solution build or pack step there.
        var nuspecOutputPath = Path.Combine(_tempDir, "nuspec-obj", projectName) + Path.DirectorySeparatorChar;

        var (exitCode, stdout, stderr) = DotnetProcess.Run(
            repoRoot,
            TimeSpan.FromMinutes(5),
            [
                "pack", projectPath, "-c", configuration, "-o", _tempDir,
                "-nodeReuse:false", "-p:UseSharedCompilation=false",
                $"-p:NuspecOutputPath={nuspecOutputPath}", $"-p:PackageVersion={PackageVersion}",
            ]);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(BuildPackFailureMessage(exitCode, stdout, stderr));
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

    internal static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Minos.NET.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find 'Minos.NET.slnx' by walking up from '{AppContext.BaseDirectory}'.");
    }
}
