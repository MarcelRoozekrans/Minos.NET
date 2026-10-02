using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ZeroAlloc.Jev.Samples;

/// <summary>Wires a sample's Jev client for its mode.</summary>
public static class SampleHost
{
    /// <summary>
    /// Registers the default <see cref="IJevClient"/> bound from <paramref name="jevSection"/>, then applies
    /// <paramref name="mode"/>. Replay needs no key, so it supplies a placeholder that is never sent anywhere.
    /// </summary>
    public static IHttpClientBuilder AddSampleJevClient(
        this IServiceCollection services,
        IConfiguration jevSection,
        SampleMode mode,
        string recordingsPath,
        string sampleName,
        RecordingSession session)
    {
        var builder = services.AddJevClient(jevSection);
        switch (mode)
        {
            case SampleMode.Replay:
                services.AddJevClient(options => options.ApiKey ??= "replay-no-key");
                builder.ConfigurePrimaryHttpMessageHandler(() => new ReplayHandler(RecordingsFile.Load(recordingsPath), sampleName));
                break;
            case SampleMode.Record:
                builder.AddHttpMessageHandler(() => new RecordingHandler(session));
                break;
        }

        return builder;
    }

    /// <summary>Builds a replaying provider from the sample's own <c>appsettings.json</c>, so tests send exactly the requests the sample recorded.</summary>
    public static ServiceProvider BuildReplayProvider(string sampleDirectory, string sampleName)
    {
        var configuration = new ConfigurationBuilder().AddJsonFile(Path.Combine(sampleDirectory, "appsettings.json")).Build();
        var services = new ServiceCollection();
        services.AddSampleJevClient(
            configuration.GetSection("Jev"), SampleMode.Replay, Path.Combine(sampleDirectory, "recordings.json"), sampleName, new RecordingSession());
        return services.BuildServiceProvider();
    }

    /// <summary>Finds <c>samples/<paramref name="sampleName"/></c> from the running assembly.</summary>
    public static string SampleDirectory(string sampleName)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ZeroAlloc.Jev.slnx")))
            {
                return Path.Combine(dir.FullName, "samples", sampleName);
            }
        }

        throw new InvalidOperationException("Could not find the repository root above " + AppContext.BaseDirectory + ".");
    }
}
