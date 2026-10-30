using System.CommandLine;
using System.Globalization;
using Minos.Evaluation;

namespace Minos.Tool;

/// <summary>What the commands share: the options they have in common, the default output directory and the mapping of failures to exit codes.</summary>
internal static class CommandSupport
{
    /// <summary>Creates the required <c>--dataset</c> option, which must name an existing file.</summary>
    public static Option<string> DatasetOption()
    {
        var dataset = new Option<string>("--dataset") { Description = "The labelled dataset: a JSONL file with one item per line.", HelpName = "file", Required = true };
        dataset.Validators.Add(result =>
        {
            var path = result.GetValueOrDefault<string>();
            if (!string.IsNullOrEmpty(path) && !File.Exists(path))
            {
                result.AddError(Directory.Exists(path)
                    ? "The dataset '" + path + "' is a directory, not a file."
                    : "The dataset '" + path + "' does not exist.");
            }
        });
        return dataset;
    }

    /// <summary>Creates the <c>--config</c> option.</summary>
    public static Option<string> ConfigOption()
        => new("--config") { Description = "The config file, JSON with a Minos section. Environment variables such as Minos__Providers__<name>__Model override it.", HelpName = "file", DefaultValueFactory = _ => ToolConfiguration.DefaultFile };

    /// <summary>Creates the <c>--provider</c> option.</summary>
    public static Option<string?> ProviderOption()
        => new("--provider") { Description = "The provider section of the config to use. Default: the section named by Minos:Active.", HelpName = "name" };

    /// <summary>Creates the <c>--model</c> option.</summary>
    public static Option<string?> ModelOption()
        => new("--model") { Description = "The model to call, in place of the section's Model. Default: the section's Model, then the provider's default model.", HelpName = "id" };

    /// <summary>Creates the <c>--prices</c> option.</summary>
    public static Option<string?> PricesOption()
        => new("--prices") { Description = "A prices file, JSON, that prices the tokens of a provider that reports no cost. Default: none, so only reported costs count.", HelpName = "file" };

    /// <summary>Creates the <c>--out</c> option.</summary>
    /// <param name="files">The files the command writes, for the description.</param>
    /// <param name="providerFrom">Where the provider in the default name comes from.</param>
    public static Option<string?> OutOption(string files, string providerFrom)
        => new("--out") { Description = "The directory for " + files + ", created when missing. Default: minos-out/<provider>-<UTC yyyyMMdd-HHmmss>, with the provider " + providerFrom + ".", HelpName = "dir" };

    /// <summary>The marker file a reserved default output directory holds, which is how two runs that start in the same second tell they collide.</summary>
    internal const string RunMarker = ".minos-run";

    /// <summary>Creates the output directory before the first request, so that one that cannot be written fails the command before a paid run, not after it.</summary>
    /// <param name="explicitOut">The <c>--out</c> directory, or <see langword="null"/> for the default one.</param>
    /// <param name="provider">The provider section name, made safe as one path segment.</param>
    /// <returns>The directory.</returns>
    /// <exception cref="IOException">The directory cannot be created.</exception>
    /// <exception cref="UnauthorizedAccessException">The directory cannot be created.</exception>
    public static string ReserveOut(string? explicitOut, string? provider)
    {
        if (explicitOut is not null)
        {
            Directory.CreateDirectory(explicitOut);
            return explicitOut;
        }

        return ReserveDefaultOut("minos-out", SafeFileName(provider), DateTime.UtcNow);
    }

    /// <summary>Reserves the default output directory, <c>&lt;root&gt;/&lt;provider&gt;-&lt;UTC time&gt;</c>, with -2, -3 and so on when it is taken. The reservation is a new marker file in the directory, which only one run can create.</summary>
    internal static string ReserveDefaultOut(string root, string provider, DateTime utcNow)
    {
        var baseName = Path.Combine(root, provider + "-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? baseName : baseName + "-" + n.ToString(CultureInfo.InvariantCulture);
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                continue;
            }

            Directory.CreateDirectory(candidate);
            try
            {
                using (new FileStream(Path.Combine(candidate, RunMarker), FileMode.CreateNew, FileAccess.Write))
                {
                }

                return candidate;
            }
            catch (IOException) when (File.Exists(Path.Combine(candidate, RunMarker)))
            {
                // Another run created the marker first.
            }
        }
    }

    /// <summary>Makes a name read from a file safe as one path segment: path separators, invalid file name characters and <c>..</c> become <c>-</c>; an empty name becomes <c>provider</c>.</summary>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    internal static string SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "provider";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) || c is '/' or '\\' ? '-' : c)])
            .Replace("..", "-", StringComparison.Ordinal);
        return cleaned.Trim() is { Length: > 0 } trimmed ? trimmed : "provider";
    }

    /// <summary>Makes a message one line, so a failure is one line on stderr.</summary>
    internal static string OneLine(string message)
        => string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Prints each problem as <c>line N: message</c>.</summary>
    public static async Task PrintProblemsAsync(TextWriter stderr, DatasetProblems problems)
    {
        foreach (var problem in problems)
        {
            await stderr.WriteLineAsync("line " + problem.Line.ToString(CultureInfo.InvariantCulture) + ": " + problem.Message).ConfigureAwait(false);
        }
    }

    /// <summary>Reads an input file, so that a failure to read it is reported as an input problem and not as an output problem.</summary>
    /// <typeparam name="T">What the read gives.</typeparam>
    /// <param name="read">The read of the input file.</param>
    /// <returns>What <paramref name="read"/> gives.</returns>
    /// <exception cref="InputReadException">The file exists but cannot be read, or is gone.</exception>
    public static T ReadInput<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException && ex is not FileLoadException))
        {
            throw new InputReadException(OneLine(ex.Message), ex);
        }
    }

    /// <summary>Reads an input file asynchronously, so that a failure to read it is reported as an input problem and not as an output problem.</summary>
    /// <typeparam name="T">What the read gives.</typeparam>
    /// <param name="read">The read of the input file.</param>
    /// <returns>What <paramref name="read"/> gives.</returns>
    /// <exception cref="InputReadException">The file exists but cannot be read, or is gone.</exception>
    public static async Task<T> ReadInputAsync<T>(Func<Task<T>> read)
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException || (ex is IOException && ex is not FileLoadException))
        {
            throw new InputReadException(OneLine(ex.Message), ex);
        }
    }

    /// <summary>Reports a cancelled command: one line on stderr, and exit code 130.</summary>
    /// <param name="stderr">Where the line goes.</param>
    public static async Task<int> CancelledAsync(TextWriter stderr)
    {
        await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
        return ExitCodes.Cancelled;
    }

    /// <summary>Runs a command and maps what escapes it to an exit code and one line on stderr.</summary>
    /// <param name="stderr">Where the failure goes.</param>
    /// <param name="failure">What failed, as the start of the line: <c>The run failed</c>.</param>
    /// <param name="execute">The command.</param>
    /// <param name="ct">The cancellation token; a command it cancels exits 130 when it throws an <see cref="OperationCanceledException"/>.</param>
    public static async Task<int> GuardAsync(TextWriter stderr, string failure, Func<Task<int>> execute, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await execute().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return await CancelledAsync(stderr).ConfigureAwait(false);
        }
        catch (InputReadException ex)
        {
            await stderr.WriteLineAsync("The input cannot be read: " + ex.Message).ConfigureAwait(false);
            return ExitCodes.Usage;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException)
        {
            // A missing input file, or an assembly that did not load: not a problem with writing the output.
            await stderr.WriteLineAsync("A file or assembly the command needs is missing or cannot be loaded: " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync("The output cannot be written: " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Failure;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync(failure + ": " + OneLine(ex.Message)).ConfigureAwait(false);
            return ExitCodes.Failure;
        }
    }
}
