using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Minos.SystemOne;

namespace Minos.Samples;

/// <summary>Wires a sample's Minos client for its mode.</summary>
public static class SampleHost
{
    /// <summary>
    /// Registers the default <see cref="IDecisionClient"/> bound from <paramref name="clientSection"/>, then applies
    /// <paramref name="mode"/>. Replay needs no key, so it supplies a placeholder that is never sent anywhere.
    /// <paramref name="recordingsPath"/> is read in replay only, so a live run may pass <see langword="null"/>.
    /// <paramref name="provider"/> names the provider of a sample that has several, for the record command in errors.
    /// </summary>
    /// <exception cref="ArgumentException">Replay is asked for without a recordings path.</exception>
    /// <exception cref="FileNotFoundException">Replay is asked for and the recordings file does not exist.</exception>
    public static SystemOneClientServiceBuilder AddSampleDecisionClient(
        this IServiceCollection services,
        IConfiguration clientSection,
        SampleMode mode,
        string? recordingsPath,
        string sampleName,
        RecordingSession session,
        string? provider = null)
    {
        if (mode == SampleMode.Replay)
        {
            ArgumentException.ThrowIfNullOrEmpty(recordingsPath);
            if (!File.Exists(recordingsPath))
            {
                throw new FileNotFoundException(
                    "No recordings for " + (provider ?? sampleName) + " at " + recordingsPath + ". Record them with: "
                    + RecordCommand(sampleName, provider),
                    recordingsPath);
            }
        }

        var builder = services.AddSystemOneClient(clientSection);
        switch (mode)
        {
            case SampleMode.Replay:
                services.AddSystemOneClient(options => options.ApiKey ??= "replay-no-key");
                builder.HttpClient.ConfigurePrimaryHttpMessageHandler(() => new ReplayHandler(RecordingsFile.Load(recordingsPath!), sampleName, provider));
                break;
            case SampleMode.Record:
                builder.HttpClient.AddHttpMessageHandler(() => new RecordingHandler(session));
                break;
        }

        return builder;
    }

    /// <summary>Builds a replaying provider from the sample's own <c>appsettings.json</c>, so tests send exactly the requests the sample recorded.</summary>
    public static ServiceProvider BuildReplayProvider(string sampleDirectory, string sampleName)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(sampleDirectory, "appsettings.json")).Build();
        var services = new ServiceCollection();
        services.AddSampleDecisionClient(
            configuration.GetSection("Minos"), SampleMode.Replay, Path.Combine(sampleDirectory, "recordings.json"), sampleName, new RecordingSession());
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The sample's <c>recordings.json</c> in its source folder, which replay reads and record rewrites, so a record run
    /// is replayed at once with no rebuild. Live mode reads no recordings and gets <see langword="null"/>, so it runs
    /// from anywhere, not only from a clone of the repository.
    /// </summary>
    public static string? RecordingsPath(SampleMode mode, string sampleName)
        => mode == SampleMode.Live ? null : Path.Combine(SampleDirectory(sampleName), "recordings.json");

    /// <summary>The command that records a sample again, naming the provider, in lower case, when the sample has several.</summary>
    public static string RecordCommand(string sampleName, string? provider = null)
        => "dotnet run --project samples/" + sampleName + " -- "
            + (provider is null ? string.Empty : "--provider " + provider.ToLowerInvariant() + " ")
            + "--record";

    /// <summary>
    /// One provider's recordings, for a sample that runs against several: <c>recordings/&lt;provider&gt;.json</c> in its
    /// source folder, in lower case. Live mode reads no recordings and gets <see langword="null"/>.
    /// </summary>
    public static string? ProviderRecordingsPath(SampleMode mode, string sampleName, string provider)
        => mode == SampleMode.Live ? null : ProviderRecordingsFile(SampleDirectory(sampleName), provider);

    /// <summary>The recordings file of <paramref name="provider"/> under <paramref name="sampleDirectory"/>.</summary>
    public static string ProviderRecordingsFile(string sampleDirectory, string provider)
    {
        ArgumentException.ThrowIfNullOrEmpty(provider);
        return Path.Combine(sampleDirectory, "recordings", provider.ToLowerInvariant() + ".json");
    }

    /// <summary>
    /// Builds a replaying provider for one provider of a sample that has several, from the sample's own
    /// <c>appsettings.json</c> with <c>Minos:Active</c> set to <paramref name="provider"/>.
    /// </summary>
    public static ServiceProvider BuildProviderReplayProvider(string sampleDirectory, string sampleName, string provider)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(sampleDirectory, "appsettings.json"))
            .AddInMemoryCollection([new KeyValuePair<string, string?>(SampleProviders.ActiveKey, provider)])
            .Build();
        var active = SampleProviders.Active(configuration);
        var services = new ServiceCollection();
        services.AddSampleDecisionClient(
            active,
            SampleMode.Replay,
            ProviderRecordingsFile(sampleDirectory, active.Key),
            sampleName,
            new RecordingSession(),
            active.Key);
        return services.BuildServiceProvider();
    }

    /// <summary>Finds <c>samples/<paramref name="sampleName"/></c> from the running assembly.</summary>
    public static string SampleDirectory(string sampleName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Minos.NET.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", sampleName);
            }
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory + ".");
    }
}
