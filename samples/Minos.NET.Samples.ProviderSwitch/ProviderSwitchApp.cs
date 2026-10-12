using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Minos.Samples.ProviderSwitch;

/// <summary>The sample's program, apart from the console, so a test can run it and read its exit code.</summary>
public static class ProviderSwitchApp
{
    public const string SampleName = "Minos.NET.Samples.ProviderSwitch";

    public const string Usage =
        "Usage: dotnet run --project samples/Minos.NET.Samples.ProviderSwitch [-- [--replay | --live | --record] [--provider <name>]]";

    /// <summary>
    /// Runs the sample against the provider that <c>Minos:Active</c> or <c>--provider</c> names. Returns 0; 1 when replay
    /// has no recordings for that provider; 2 for a bad argument, or a provider that is unknown or not set.
    /// </summary>
    public static async Task<int> RunAsync(
        IReadOnlyList<string> args, string contentRoot, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        SampleArguments arguments;
        try
        {
            arguments = SampleArguments.Parse(args);
        }
        catch (ArgumentException e)
        {
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            await error.WriteLineAsync(Usage).ConfigureAwait(false);
            return 2;
        }

        // No Args: the host's command-line provider would read "--record --provider local" as record=--provider. The
        // --provider value is added last, after the environment, so it wins over Minos__Active.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { ContentRootPath = contentRoot });
        if (arguments.Provider is { } chosen)
        {
            builder.Configuration.AddInMemoryCollection([new KeyValuePair<string, string?>(SampleProviders.ActiveKey, chosen)]);
        }

        IConfigurationSection active;
        try
        {
            active = SampleProviders.Active(builder.Configuration);
        }
        catch (InvalidOperationException e)
        {
            builder.Configuration.Dispose();
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            return 2;
        }

        // Replay and record use the recordings in the repository checkout; live needs none, so it runs from anywhere.
        var provider = active.Key;
        var recordingsPath = SampleHost.ProviderRecordingsPath(arguments.Mode, SampleName, provider);
        var session = new RecordingSession();
        try
        {
            builder.Services.AddSampleDecisionClient(active, arguments.Mode, recordingsPath, SampleName, session, provider);
        }
        catch (FileNotFoundException e)
        {
            builder.Configuration.Dispose();
            await error.WriteLineAsync(e.Message).ConfigureAwait(false);
            return 1;
        }

        using var host = builder.Build();
        var client = host.Services.GetRequiredService<IDecisionClient>();
        var report = await ProviderSwitchSample.RunAsync(client, provider, cancellationToken).ConfigureAwait(false);
        await output.WriteAsync(report.Render()).ConfigureAwait(false);

        if (arguments.Mode == SampleMode.Record && recordingsPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(recordingsPath)!);
            session.ToFile(provider, DateOnly.FromDateTime(DateTime.UtcNow)).Save(recordingsPath);
            await output.WriteLineAsync("Recorded " + session.Count + " responses to " + recordingsPath).ConfigureAwait(false);
        }

        return 0;
    }
}
