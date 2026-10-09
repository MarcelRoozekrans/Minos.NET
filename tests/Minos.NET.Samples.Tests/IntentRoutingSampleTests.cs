using Microsoft.Extensions.DependencyInjection;
using Minos.Samples.IntentRouting;
using ZeroAlloc.TestHelpers;

namespace Minos.Samples.Tests;

public sealed class IntentRoutingSampleTests
{
    private const string Sample = "Minos.NET.Samples.IntentRouting";

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
    public async Task EightOfTwelve_NeedNoLanguageModel()
    {
        var report = await Run();

        Assert.Equal(8, report.WithoutLanguageModel);
        Assert.Equal("8 of 12 requests needed no language model", report.Render().TrimEnd('\n').Split('\n')[^1]);
    }

    [Theory]
    [InlineData("t01", RequestHandler.BookingLookup, false, ConfidenceTier.High)]
    [InlineData("t02", RequestHandler.BookingLookup, true, ConfidenceTier.High)]
    [InlineData("t03", RequestHandler.AssistantModel, false, ConfidenceTier.High)]
    [InlineData("t04", RequestHandler.AssistantModel, false, ConfidenceTier.High)]
    [InlineData("t05", RequestHandler.AssistantModel, true, ConfidenceTier.High)]
    [InlineData("t06", RequestHandler.Person, false, ConfidenceTier.High)]
    [InlineData("t07", RequestHandler.Person, false, ConfidenceTier.High)]
    [InlineData("t08", RequestHandler.BookingLookup, false, ConfidenceTier.High)]
    [InlineData("t09", RequestHandler.Person, false, ConfidenceTier.High)]
    [InlineData("t10", RequestHandler.BookingLookup, true, ConfidenceTier.High)]
    [InlineData("t11", RequestHandler.AssistantModel, false, ConfidenceTier.High)]
    [InlineData("t12", RequestHandler.Person, false, ConfidenceTier.High)]
    public async Task EveryRecordedRequest_RoutesAsRecorded(string id, RequestHandler handler, bool urgent, ConfidenceTier tier)
    {
        var routed = Find(await Run(), id);

        Assert.Equal(handler, routed.Handler);
        Assert.Equal(urgent, routed.Urgent);
        Assert.Equal(tier, routed.Tier);
    }

    [Fact]
    public void Report_CountFollowsTheRequests_AfterAWith()
    {
        var report = new RoutingReport([new RoutedRequest("t", "x", TravelIntent.ChangeBooking, ConfidenceTier.High, RequestHandler.AssistantModel, false)]);
        var changed = report with { Requests = [new RoutedRequest("t", "x", TravelIntent.DisputeCharge, ConfidenceTier.High, RequestHandler.Person, false)] };

        Assert.Equal(0, report.WithoutLanguageModel);
        Assert.Equal(1, changed.WithoutLanguageModel);
        Assert.EndsWith("1 of 1 requests needed no language model\n", changed.Render(), StringComparison.Ordinal);
    }

    private static RoutedRequest Find(RoutingReport report, string id) =>
        report.Requests.First(x => string.Equals(x.Id, id, StringComparison.Ordinal));

    [Fact]
    public async Task Report_MatchesTheSnapshot() => TextSnapshot.VerifyText((await Run()).Render());

    private static async Task<RoutingReport> Run()
    {
        using var provider = SampleHost.BuildReplayProvider(SampleHost.SampleDirectory(Sample), Sample);
        return await IntentRoutingSample.RunAsync(provider.GetRequiredService<IJevClient>(), CancellationToken.None);
    }
}
