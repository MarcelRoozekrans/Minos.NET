using System.Globalization;
using ZeroAlloc.Jev.Samples.IntentRouting;

namespace ZeroAlloc.Jev.Samples.Tests;

public sealed class TravelRoutingTests
{
    [Fact]
    public async Task LowConfidence_GoesToAPerson()
    {
        var route = TravelRouting.Route(await Request("look_up_booking", 0.4));

        Assert.Equal(RequestHandler.Person, route.Handler);
        Assert.Equal(ConfidenceTier.Low, route.Tier);
    }

    [Fact]
    public async Task MediumBoundary_IsInclusive_AndRoutesByIntent()
    {
        var route = TravelRouting.Route(await Request("look_up_booking", 0.5));

        Assert.Equal(ConfidenceTier.Medium, route.Tier);
        Assert.Equal(RequestHandler.BookingLookup, route.Handler);
    }

    [Fact]
    public async Task JustBelowMediumBoundary_IsLow_AndGoesToAPerson()
    {
        var route = TravelRouting.Route(await Request("look_up_booking", 0.4999));

        Assert.Equal(ConfidenceTier.Low, route.Tier);
        Assert.Equal(RequestHandler.Person, route.Handler);
    }

    [Fact]
    public async Task HighConfidence_IsHigh()
    {
        var route = TravelRouting.Route(await Request("look_up_booking", 0.9));

        Assert.Equal(ConfidenceTier.High, route.Tier);
        Assert.Equal(RequestHandler.BookingLookup, route.Handler);
    }

    [Fact]
    public async Task MediumConfidence_StillRoutesByIntent()
    {
        var route = TravelRouting.Route(await Request("change_booking", 0.7));

        Assert.Equal(RequestHandler.AssistantModel, route.Handler);
    }

    [Fact]
    public async Task UrgencyThreshold_IsInclusive()
    {
        Assert.True(TravelRouting.Route(await Request("look_up_booking", 0.9, travelsWithin24Hours: 0.5)).Urgent);
    }

    [Fact]
    public async Task JustBelowUrgencyThreshold_IsNotUrgent()
    {
        Assert.False(TravelRouting.Route(await Request("look_up_booking", 0.9, travelsWithin24Hours: 0.49)).Urgent);
    }

    [Theory]
    [InlineData("other")]
    [InlineData("dispute_charge")]
    public async Task OtherAndDisputes_AtHighConfidence_GoToAPerson(string intent)
    {
        var route = TravelRouting.Route(await Request(intent, 0.95));

        Assert.Equal(ConfidenceTier.High, route.Tier);
        Assert.Equal(RequestHandler.Person, route.Handler);
    }

    private static Task<TravelRequest> Request(string intent, double confidence, double travelsWithin24Hours = 0) =>
        Canned.EvaluateAsync<TravelRequest>(string.Create(
            CultureInfo.InvariantCulture,
            $$$"""
            {"model":"canned","answers":{
              "intent":{"type":"choice","choice":"{{{intent}}}",
                "probabilities":{"change_booking":0.25,"dispute_charge":0.25,"other":0.25,"look_up_booking":0.25},
                "confidence":{{{confidence}}}},
              "travels_within24_hours":{"type":"noul","noul":{{{travelsWithin24Hours}}}}},
             "usage":{"input_tokens":1,"output_tokens":1}}
            """));
}
