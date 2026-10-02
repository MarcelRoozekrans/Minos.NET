using Microsoft.Extensions.DependencyInjection;
using ZeroAlloc.Jev.Samples.IntentRouting;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class IntentRoutingSampleTests
{
    private const string Sample = "ZeroAlloc.Jev.Samples.IntentRouting";

    [Fact]
    public async Task Disputes_GoToAPerson()
    {
        var report = await Run();

        Assert.Equal(RequestHandler.Person, Find(report, "t06").Handler);
        Assert.Equal(RequestHandler.Person, Find(report, "t07").Handler);
    }

    [Fact]
    public async Task Lookups_NeedNoLanguageModel()
    {
        var report = await Run();

        Assert.Equal(RequestHandler.BookingLookup, Find(report, "t01").Handler);
    }

    [Fact]
    public async Task Changes_GoToTheAssistant()
    {
        var report = await Run();

        Assert.Equal(RequestHandler.AssistantModel, Find(report, "t03").Handler);
        Assert.Equal(RequestHandler.AssistantModel, Find(report, "t04").Handler);
    }

    [Fact]
    public async Task ImminentTravel_IsUrgent()
    {
        var report = await Run();

        Assert.True(Find(report, "t05").Urgent);
    }

    [Fact]
    public async Task TheCountMatchesTheRoutes()
    {
        var report = await Run();

        Assert.Equal(report.Requests.Count(r => r.Handler != RequestHandler.AssistantModel), report.WithoutLanguageModel);
        Assert.EndsWith(report.WithoutLanguageModel + " of 12 requests needed no language model\n", report.Render(), StringComparison.Ordinal);
    }

    private static RoutedRequest Find(RoutingReport report, string id) =>
        report.Requests.First(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    private static async Task<RoutingReport> Run()
    {
        using var provider = SampleHost.BuildReplayProvider(SampleHost.SampleDirectory(Sample), Sample);
        return await IntentRoutingSample.RunAsync(provider.GetRequiredService<IJevClient>(), CancellationToken.None);
    }
}
