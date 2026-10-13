using Minos.Conformance;
using Minos.SystemOne;

namespace Minos.Docs.Tests;

public static class ConformanceRecording
{
    #region Conformance_Record
    // Records every probe against a /v1/systemone server of your own, then writes the report next to the fixture.
    public static async Task<ConformanceReport> RecordAsync(Uri server, string model, string fixturePath)
    {
        var target = new ConformanceTarget(
            "my-server",
            ConformanceWire.SystemOne,
            model,
            new ModelRule(
                "The server reports the model it was asked for.",
                static (requested, reported) => string.Equals(requested, reported, StringComparison.Ordinal)),
            http =>
            {
                // No retries, so each probe is sent once.
                var options = new SystemOneDecisionClientOptions
                {
                    Provider = SystemOneProvider.Custom,
                    BaseAddress = server,
                    Model = model,
                    MaxRetries = 0,
                };
                SystemOneDecisionClient.ConfigureHttpClient(http, options);
                return new SystemOneDecisionClient(http, options);
            });

        var report = await new ConformanceRunner().RunAsync(target, ConformanceMode.Record, fixturePath);
        await File.WriteAllTextAsync(Path.ChangeExtension(fixturePath, ".md"), report.ToMarkdown());
        return report;
    }
    #endregion
}
