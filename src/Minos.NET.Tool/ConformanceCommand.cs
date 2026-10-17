using System.CommandLine;
using Minos.Conformance;
using Minos.SystemOne;

namespace Minos.Tool;

/// <summary>The <c>minos conformance</c> command: checks a live endpoint against the conformance suite.</summary>
internal static class ConformanceCommand
{
    public static Command Create(TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network)
    {
        var config = CommandSupport.ConfigOption();
        var provider = CommandSupport.ProviderOption();
        var model = CommandSupport.ModelOption();
        var output = CommandSupport.OutOption("conformance.json and conformance.md", "section being checked");

        var command = new Command("conformance", "Sends the conformance probes to a live provider and checks its answers. Exits 1 when a check fails; a skipped check passes.")
        {
            config, provider, model, output,
        };
        command.SetAction((parse, ct) => CommandSupport.GuardAsync(
            stderr,
            "The conformance run failed",
            () => ExecuteAsync(parse.GetRequiredValue(config), parse.GetValue(provider), parse.GetValue(model), parse.GetValue(output), stdout, stderr, network, ct),
            ct));
        return command;
    }

    private static async Task<int> ExecuteAsync(
        string configPath, string? providerOption, string? modelOption, string? outOption, TextWriter stdout, TextWriter stderr, Func<HttpMessageHandler> network, CancellationToken ct)
    {
        var resolved = ToolConfiguration.Resolve(configPath, providerOption, modelOption);
        if (!resolved.IsSuccess)
        {
            await stderr.WriteLineAsync(resolved.Error).ConfigureAwait(false);
            return ExitCodes.Usage;
        }

        var (name, options) = resolved.Value;
        var target = new ConformanceTarget(
            name,
            ConformanceWire.SystemOne,
            string.IsNullOrWhiteSpace(options.Model) ? SystemOneDefaults.Model : options.Model,
            options.Provider switch
            {
                SystemOneProvider.TypeSafe => ModelRule.TypeSafe,
                SystemOneProvider.OpenRouter => ModelRule.OpenRouter,
                _ => null,
            },
            http =>
            {
                SystemOneDecisionClient.ConfigureHttpClient(http, options);
                return new SystemOneDecisionClient(http, options);
            });

        await stdout.WriteLineAsync("Checking provider " + name + " with model " + target.Model + ".").ConfigureAwait(false);
        var report = await new ConformanceRunner(network, TimeProvider.System).RunAsync(target, ConformanceMode.Live, null, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        var outDir = outOption ?? CommandSupport.NextDefaultOut("minos-out", name, DateTime.UtcNow);
        Directory.CreateDirectory(outDir);
        var json = Path.Combine(outDir, "conformance.json");
        var markdown = Path.Combine(outDir, "conformance.md");
        await File.WriteAllTextAsync(json, report.ToJson(), ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(markdown, report.ToMarkdown(), ct).ConfigureAwait(false);
        await stdout.WriteLineAsync(json).ConfigureAwait(false);
        await stdout.WriteLineAsync(markdown).ConfigureAwait(false);
        await stdout.WriteLineAsync("Passed " + report.Passed + ", failed " + report.Failed + ", skipped " + report.Skipped + ".").ConfigureAwait(false);

        if (report.Failed > 0)
        {
            await stderr.WriteLineAsync("Failed checks: " + string.Join(", ", report.FailedChecks) + ".").ConfigureAwait(false);
            return ExitCodes.Failure;
        }

        return ExitCodes.Success;
    }
}
