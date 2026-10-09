using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Minos;
using Minos.Samples;
using Minos.Samples.Reranking;

const string SampleName = "Minos.NET.Samples.Reranking";

SampleMode mode;
try
{
    mode = SampleModes.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("Usage: dotnet run --project samples/Minos.NET.Samples.Reranking [-- --replay | --live | --record]");
    return 2;
}

// Replay and record use the recordings in the repository checkout; live needs none, so it runs from anywhere.
var recordingsPath = SampleHost.RecordingsPath(mode, SampleName);

var session = new RecordingSession();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSampleDecisionClient(builder.Configuration.GetSection("Jev"), mode, recordingsPath, SampleName, session);
using var host = builder.Build();

var jev = host.Services.GetRequiredService<IDecisionClient>();
var report = await RerankingSample.RunAsync(jev, CancellationToken.None).ConfigureAwait(false);
Console.Write(report.Render());

if (mode == SampleMode.Record && recordingsPath is not null)
{
    session.ToFile("OpenRouter", DateOnly.FromDateTime(DateTime.UtcNow)).Save(recordingsPath);
    Console.WriteLine("Recorded " + session.Count + " responses to " + recordingsPath);
}

return 0;
