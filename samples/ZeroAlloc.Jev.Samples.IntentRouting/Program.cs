using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Jev;
using ZeroAlloc.Jev.Samples;
using ZeroAlloc.Jev.Samples.IntentRouting;

const string SampleName = "ZeroAlloc.Jev.Samples.IntentRouting";

SampleMode mode;
try
{
    mode = SampleModes.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("Usage: dotnet run --project samples/ZeroAlloc.Jev.Samples.IntentRouting [-- --replay | --live | --record]");
    return 2;
}

// The recordings live in the source folder in every mode, so a record run is replayed at once, with no rebuild.
var recordingsPath = Path.Combine(SampleHost.SampleDirectory(SampleName), "recordings.json");

var session = new RecordingSession();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSampleJevClient(builder.Configuration.GetSection("Jev"), mode, recordingsPath, SampleName, session);
using var host = builder.Build();

var jev = host.Services.GetRequiredService<IJevClient>();
var report = await IntentRoutingSample.RunAsync(jev, CancellationToken.None).ConfigureAwait(false);
Console.Write(report.Render());

if (mode == SampleMode.Record)
{
    session.ToFile("OpenRouter", DateOnly.FromDateTime(DateTime.UtcNow)).Save(recordingsPath);
    Console.WriteLine("Recorded " + session.Count + " responses to " + recordingsPath);
}

return 0;
