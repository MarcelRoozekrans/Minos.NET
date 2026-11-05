using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Fake = Minos.Tests.EscalationTests.Fake;

namespace Minos.Tests;

/// <summary>The core <c>UseEscalation</c> builder extensions.</summary>
public sealed class EscalationBuilderTests
{
    private static readonly DecisionContent State = DecisionContent.FromString("Card was charged twice");

    private static readonly Dictionary<string, QuestionAnswer> UnsurePrimary = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.97),
        ["department"] = QuestionAnswer.Choice(0, 0.55, [0.55, 0.4, 0.05]),
        ["severity"] = QuestionAnswer.Score(2, 1.9, 0.9, [0.02, 0.08, 0.9]),
    };

    private static readonly Dictionary<string, QuestionAnswer> TargetAnswers = new(StringComparer.Ordinal)
    {
        ["is_urgent"] = QuestionAnswer.Noul(0.2),
        ["department"] = QuestionAnswer.Choice(1, 0.8, [0.1, 0.8, 0.1]),
        ["severity"] = QuestionAnswer.Score(1, 1.0, 0.8, [0.1, 0.8, 0.1]),
    };

    [Fact]
    public async Task UseEscalation_with_a_client_re_asks_on_it()
    {
        var target = Fake.Answering(TargetAnswers);
        using var client = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(target, o => o.TargetName = "hosted")
            .Build();

        var response = (await client.EvaluateAsync(new DecisionRequest(QuestionSets.TriageDefinition(), State))).Value;

        Assert.Equal(new QuestionRoute("department", "hosted", RouteReason.Escalated, 0.55, 0.8), response.Path!.Routes[1]);
    }

    [Fact]
    public void UseEscalation_with_a_factory_creates_the_target_from_the_builds_services()
    {
        var target = Fake.Answering(TargetAnswers);
        var services = new Services { [typeof(IDecisionClient)] = target };
        IServiceProvider? seen = null;

        using var client = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(
                s =>
                {
                    seen = s;
                    return (IDecisionClient)s.GetService(typeof(IDecisionClient))!;
                },
                o => o.TargetName = "hosted")
            .Build(services);

        Assert.Same(services, seen);
        Assert.NotNull(client.GetService<EscalatingDecisionClient>());
    }

    [Fact]
    public void UseEscalation_takes_the_logger_and_TimeProvider_from_the_services()
    {
        var time = new FakeTimeProvider();
        var services = new Services { [typeof(ILoggerFactory)] = NullLoggerFactory.Instance, [typeof(TimeProvider)] = time };

        using var client = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(Fake.Answering(TargetAnswers), o => o.TargetName = "hosted")
            .Build(services);

        var stage = client.GetService<EscalatingDecisionClient>()!;
        Assert.Same(time, stage.TimeProvider);
        Assert.NotNull(stage.Logger);
    }

    [Fact]
    public void Without_services_the_stage_reads_the_system_clock_and_logs_nowhere()
    {
        using var client = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(Fake.Answering(TargetAnswers), o => o.TargetName = "hosted")
            .Build();

        var stage = client.GetService<EscalatingDecisionClient>()!;
        Assert.Same(TimeProvider.System, stage.TimeProvider);
        Assert.Null(stage.Logger);
    }

    [Fact]
    public void A_second_UseEscalation_throws_at_Build()
    {
        var builder = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(Fake.Answering(TargetAnswers), o => o.TargetName = "clef")
            .UseRetries()
            .UseEscalation(Fake.Answering(TargetAnswers), o => o.TargetName = "hosted");

        var exception = Assert.Throws<InvalidOperationException>(() => builder.Build());

        Assert.Contains("one escalation stage", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_options_throw_ArgumentException_at_Build()
    {
        var builder = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(Fake.Answering(TargetAnswers), o =>
            {
                o.TargetName = "hosted";
                o.DefaultThreshold = 2;
            });

        var exception = Assert.ThrowsAny<ArgumentException>(() => builder.Build());

        Assert.Equal("options", exception.ParamName);
        Assert.Contains("DefaultThreshold", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_factory_returning_null_throws_at_Build()
    {
        var builder = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(_ => null!, o => o.TargetName = "hosted");

        Assert.Throws<InvalidOperationException>(() => builder.Build());
    }

    [Fact]
    public void Null_arguments_throw()
    {
        var builder = new DecisionClientBuilder(Fake.Answering(UnsurePrimary));
        var target = Fake.Answering(TargetAnswers);
        Action<EscalationOptions> configure = o => o.TargetName = "hosted";

        Assert.Equal("builder", Assert.Throws<ArgumentNullException>(() => ((DecisionClientBuilder)null!).UseEscalation(target, configure)).ParamName);
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => builder.UseEscalation((IDecisionClient)null!, configure)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.UseEscalation(target, null!)).ParamName);
        Assert.Equal("builder", Assert.Throws<ArgumentNullException>(() => ((DecisionClientBuilder)null!).UseEscalation(_ => target, configure)).ParamName);
        Assert.Equal("target", Assert.Throws<ArgumentNullException>(() => builder.UseEscalation((Func<IServiceProvider, IDecisionClient>)null!, configure)).ParamName);
        Assert.Equal("configure", Assert.Throws<ArgumentNullException>(() => builder.UseEscalation(_ => target, null!)).ParamName);
    }

    [Fact]
    public void Each_Build_configures_new_options()
    {
        var calls = 0;
        var builder = new DecisionClientBuilder(Fake.Answering(UnsurePrimary))
            .UseEscalation(Fake.Answering(TargetAnswers), o =>
            {
                calls++;
                o.TargetName = "hosted";
            });

        using var first = builder.Build();
        using var second = builder.Build();

        Assert.Equal(2, calls);
    }

    private sealed class Services : Dictionary<Type, object>, IServiceProvider
    {
        public object? GetService(Type serviceType) => TryGetValue(serviceType, out var service) ? service : null;
    }
}
