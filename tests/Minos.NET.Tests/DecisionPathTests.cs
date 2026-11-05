namespace Minos.Tests;

public sealed class DecisionPathTests
{
    private static readonly DecisionCall PrimaryCall = new("primary", "jev-latest", new DecisionUsage { InputTokens = 10 }, IsCached: false);

    private static QuestionRoute Route(string key = "is_urgent") => new(key, "primary", RouteReason.Confident, 0.9, null);

    private static DecisionResponse UrgencyResponse()
        => new(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)], model: "jev-latest");

    [Fact]
    public void Route_equality_compares_every_field()
    {
        var route = new QuestionRoute("is_urgent", "strong", RouteReason.Escalated, 0.4, 0.8);
        var same = new QuestionRoute("is_urgent", "strong", RouteReason.Escalated, 0.4, 0.8);

        Assert.Equal("is_urgent", route.Key);
        Assert.Equal("strong", route.AnsweredBy);
        Assert.Equal(RouteReason.Escalated, route.Reason);
        Assert.Equal(0.4, route.PrimaryCertainty);
        Assert.Equal(0.8, route.TargetCertainty);
        Assert.True(route.Equals(same));
        Assert.True(route.Equals((object)same));
        Assert.True(route == same);
        Assert.False(route != same);
        Assert.Equal(route.GetHashCode(), same.GetHashCode());

        QuestionRoute[] different =
        [
            new("department", "strong", RouteReason.Escalated, 0.4, 0.8),
            new("is_urgent", "primary", RouteReason.Escalated, 0.4, 0.8),
            new("is_urgent", "strong", RouteReason.TargetFailed, 0.4, 0.8),
            new("is_urgent", "strong", RouteReason.Escalated, 0.5, 0.8),
            new("is_urgent", "strong", RouteReason.Escalated, 0.4, 0.7),
            new("is_urgent", "strong", RouteReason.Escalated, 0.4, null),
        ];
        foreach (var other in different)
        {
            Assert.False(route.Equals(other));
            Assert.False(route == other);
            Assert.True(route != other);
        }

        Assert.False(route.Equals("is_urgent"));
    }

    [Fact]
    public void Route_rejects_a_null_name_or_a_certainty_out_of_range()
    {
        Assert.Throws<ArgumentNullException>(() => new QuestionRoute(null!, "primary", RouteReason.Confident, 0.9, null));
        Assert.Throws<ArgumentNullException>(() => new QuestionRoute("is_urgent", null!, RouteReason.Confident, 0.9, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionRoute("is_urgent", "primary", RouteReason.Confident, -0.1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionRoute("is_urgent", "primary", RouteReason.Confident, 1.1, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionRoute("is_urgent", "primary", RouteReason.Confident, double.NaN, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionRoute("is_urgent", "strong", RouteReason.Escalated, 0.4, 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuestionRoute("is_urgent", "strong", RouteReason.Escalated, 0.4, double.NaN));

        var bounds = new QuestionRoute("is_urgent", "strong", RouteReason.Escalated, 0, 1);
        Assert.Equal(0, bounds.PrimaryCertainty);
        Assert.Equal(1, bounds.TargetCertainty);
    }

    [Fact]
    public void Call_rejects_a_null_client_name()
    {
        Assert.Throws<ArgumentNullException>(() => new DecisionCall(null!, "jev-latest", null, IsCached: false));
        Assert.Throws<ArgumentNullException>(() => PrimaryCall with { ClientName = null! });
    }

    [Fact]
    public void Path_rejects_a_default_route()
    {
        var exception = Assert.Throws<ArgumentException>(() => new DecisionPath([Route(), default], [PrimaryCall]));

        Assert.Equal("routes", exception.ParamName);
        Assert.DoesNotContain("is_urgent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_copies_its_lists()
    {
        var routes = new List<QuestionRoute> { Route() };
        var calls = new List<DecisionCall> { PrimaryCall };

        var path = new DecisionPath(routes, calls);
        routes.Add(Route("department"));
        routes[0] = Route("other");
        calls.Clear();

        Assert.Equal([Route()], path.Routes);
        Assert.Equal([PrimaryCall], path.Calls);
        Assert.Equal("primary", path.Calls[0].ClientName);
    }

    [Fact]
    public void A_call_defaults_to_Answered_with_no_error_kind()
    {
        Assert.Equal(CallOutcome.Answered, PrimaryCall.Outcome);
        Assert.Null(PrimaryCall.ErrorKind);
        Assert.Equal(CallOutcome.Answered, default(CallOutcome));

        var failed = PrimaryCall with { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Timeout };
        Assert.Equal(CallOutcome.Failed, failed.Outcome);
        Assert.Equal(DecisionErrorKind.Timeout, failed.ErrorKind);
    }

    [Fact]
    public void Calls_with_different_outcomes_are_not_equal()
    {
        var answered = new DecisionCall("clef", null, null, IsCached: false);
        var failed = answered with { Outcome = CallOutcome.Failed };
        var skipped = answered with { Outcome = CallOutcome.NotCalledNoTimeLeft };
        var failedServer = failed with { ErrorKind = DecisionErrorKind.Server };
        var failedTimeout = failed with { ErrorKind = DecisionErrorKind.Timeout };

        Assert.NotEqual(answered, failed);
        Assert.NotEqual(answered, skipped);
        Assert.NotEqual(failed, skipped);
        Assert.NotEqual(failed, failedServer);
        Assert.NotEqual(failedServer, failedTimeout);
        Assert.Equal(failedServer, new DecisionCall("clef", null, null, IsCached: false) { Outcome = CallOutcome.Failed, ErrorKind = DecisionErrorKind.Server });
        Assert.Equal(failedServer.GetHashCode(), (failed with { ErrorKind = DecisionErrorKind.Server }).GetHashCode());
    }

    [Fact]
    public void Path_rejects_no_calls()
    {
        Assert.Throws<ArgumentNullException>(() => new DecisionPath(null!, [PrimaryCall]));
        Assert.Throws<ArgumentNullException>(() => new DecisionPath([Route()], null!));
        Assert.Throws<ArgumentException>(() => new DecisionPath([Route()], []));
    }

    [Fact]
    public void Response_Path_rejects_a_route_count_or_key_mismatch()
    {
        var tooFew = new DecisionPath([], [PrimaryCall]);
        var tooMany = new DecisionPath([Route(), Route("department")], [PrimaryCall]);
        var wrongKey = new DecisionPath([Route("department")], [PrimaryCall]);

        Assert.Throws<ArgumentException>(() => new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = tooFew });
        Assert.Throws<ArgumentException>(() => new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = tooMany });
        Assert.Throws<ArgumentException>(() => new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = wrongKey });

        var path = new DecisionPath([Route()], [PrimaryCall]);
        var response = new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = path };
        Assert.Same(path, response.Path);
    }

    [Fact]
    public void Response_Path_message_carries_no_question_key()
    {
        var wrongKey = new DecisionPath([Route("secret_key")], [PrimaryCall]);

        var exception = Assert.Throws<ArgumentException>(() => new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = wrongKey });

        Assert.DoesNotContain("secret_key", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("is_urgent", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AsCached_keeps_the_path()
    {
        var path = new DecisionPath([Route()], [PrimaryCall]);
        var response = new DecisionResponse(QuestionSets.UrgencyDefinition(), [QuestionAnswer.Noul(0.75)]) { Path = path };

        var cached = response.AsCached();

        Assert.True(cached.IsCached);
        Assert.Same(path, cached.Path);
        Assert.Null(UrgencyResponse().AsCached().Path);
    }

    [Fact]
    public void Certainty_of_a_Noul_is_the_larger_side()
    {
        Assert.Equal(0.95, DecisionPath.Certainty(QuestionKind.Noul, new AnswerSlot(0, 0.05, 0, 0)), 12);
        Assert.Equal(0.95, DecisionPath.Certainty(QuestionKind.Noul, new AnswerSlot(0, 0.95, 0, 0)), 12);
        Assert.Equal(0.5, DecisionPath.Certainty(QuestionKind.Noul, new AnswerSlot(0, 0.5, 0, 0)));
        Assert.Equal(0.7, DecisionPath.Certainty(QuestionKind.Choice, new AnswerSlot(1, 0, 0.7, 0)));
        Assert.Equal(0.6, DecisionPath.Certainty(QuestionKind.Score, new AnswerSlot(2, 1.8, 0.6, 3)));
    }

    [Fact]
    public async Task A_response_without_a_stage_has_no_path()
    {
        Assert.Null(UrgencyResponse().Path);

        using var client = new ClientTestKit.CapturingClient();
        var result = await client.EvaluateAsync(new DecisionRequest(QuestionSets.UrgencyDefinition(), "s"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Path);
    }
}
