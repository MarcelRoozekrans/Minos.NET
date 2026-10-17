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

    /// <summary>The default output directory, <c>&lt;root&gt;/&lt;provider&gt;-&lt;UTC time&gt;</c>, with -2, -3 and so on when it exists.</summary>
    internal static string NextDefaultOut(string root, string provider, DateTime utcNow)
    {
        var baseName = Path.Combine(root, provider + "-" + utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
        var candidate = baseName;
        for (var n = 2; Directory.Exists(candidate) || File.Exists(candidate); n++)
        {
            candidate = baseName + "-" + n.ToString(CultureInfo.InvariantCulture);
        }

        return candidate;
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

    /// <summary>Runs a command and maps what escapes it to an exit code and one line on stderr.</summary>
    /// <param name="stderr">Where the failure goes.</param>
    /// <param name="failure">What failed, as the start of the line: <c>The run failed</c>.</param>
    /// <param name="execute">The command.</param>
    /// <param name="ct">The cancellation token; a command it cancels exits 130, whatever it threw.</param>
    public static async Task<int> GuardAsync(TextWriter stderr, string failure, Func<Task<int>> execute, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            return await execute().ConfigureAwait(false);
        }
        catch (Exception ex) when (ct.IsCancellationRequested && ex is OperationCanceledException or DatasetRunException)
        {
            await stderr.WriteLineAsync("Cancelled.").ConfigureAwait(false);
            return ExitCodes.Cancelled;
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
