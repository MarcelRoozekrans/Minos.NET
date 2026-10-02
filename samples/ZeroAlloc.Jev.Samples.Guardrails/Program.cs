using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZeroAlloc.Jev;
using ZeroAlloc.Jev.Samples;
using ZeroAlloc.Jev.Samples.Guardrails;

const string SampleName = "ZeroAlloc.Jev.Samples.Guardrails";

SampleMode mode;
try
{
    mode = SampleModes.Parse(args);
}
catch (ArgumentException e)
{
    Console.Error.WriteLine(e.Message);
    Console.Error.WriteLine("Usage: dotnet run --project samples/ZeroAlloc.Jev.Samples.Guardrails [-- --replay | --live | --record]");
    return 2;
}

// Replay and record use the recordings in the repository checkout; live needs none, so it runs from anywhere.
var recordingsPath = SampleHost.RecordingsPath(mode, SampleName);

var session = new RecordingSession();
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Services.AddSampleJevClient(builder.Configuration.GetSection("Jev"), mode, recordingsPath, SampleName, session);
using var host = builder.Build();

var jev = host.Services.GetRequiredService<IJevClient>();
var report = await GuardrailsSample.RunAsync(jev, CancellationToken.None).ConfigureAwait(false);
Console.Write(report.Render());

if (mode == SampleMode.Record && recordingsPath is not null)
{
    session.ToFile("OpenRouter", DateOnly.FromDateTime(DateTime.UtcNow)).Save(recordingsPath);
    Console.WriteLine("Recorded " + session.Count + " responses to " + recordingsPath);
}

return 0;
