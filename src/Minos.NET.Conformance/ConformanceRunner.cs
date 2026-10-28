using System.Globalization;

namespace Minos.Conformance;

/// <summary>Runs every probe and check against a target, live, recording or from a recording, and returns the report.</summary>
public sealed class ConformanceRunner
{
    private readonly Func<HttpMessageHandler> _network;
    private readonly TimeProvider _time;

    /// <summary>Initializes a new instance of the <see cref="ConformanceRunner"/> class that calls the network with a new <see cref="SocketsHttpHandler"/> per run.</summary>
    public ConformanceRunner()
        : this(static () => new SocketsHttpHandler(), TimeProvider.System)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="ConformanceRunner"/> class.</summary>
    /// <param name="network">Creates the handler a live or recording run sends through; the run disposes it.</param>
    /// <param name="time">The clock that dates a recording.</param>
    public ConformanceRunner(Func<HttpMessageHandler> network, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(time);

        _network = network;
        _time = time;
    }

    /// <summary>Runs every probe that applies to the target's capabilities, and every check that applies to each.</summary>
    /// <param name="target">The endpoint to check.</param>
    /// <param name="mode">Live, record or replay.</param>
    /// <param name="fixturePath">The fixture to replay, or to write when recording; ignored for a live run.</param>
    /// <param name="cancellationToken">Cancels the run.</param>
    /// <returns>The report.</returns>
    /// <exception cref="ArgumentException">A record or replay run has no <paramref name="fixturePath"/>.</exception>
    /// <exception cref="InvalidDataException">The replayed fixture is for another target, names a probe that does not exist or twice, or has no exchange for a probe that applies.</exception>
    public async Task<ConformanceReport> RunAsync(
        ConformanceTarget target, ConformanceMode mode, string? fixturePath = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (mode != ConformanceMode.Live)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);
        }

        var fixture = mode == ConformanceMode.Replay ? ConformanceFixture.Load(fixturePath!) : null;
        if (fixture is not null && !string.Equals(fixture.Target, target.Name, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The fixture is for " + fixture.Target + ", not " + target.Name + ".");
        }

        using var capture = new ExchangeCapture(fixture is null ? _network() : new FixtureHandler(fixture, target.Wire));
        using var http = new HttpClient(capture, disposeHandler: false);
        using var client = target.CreateClient(http);
        var capabilities = client.GetService<DecisionCapabilities>();
        if (fixture is not null)
        {
            CheckCovers(fixture, capabilities, target.Wire);
        }

        var probes = new List<ProbeReport>();
        var outcomes = new List<ProbeOutcome>();
        var recorded = new List<ConformanceExchange>();
        foreach (var probe in ConformanceProbe.All)
        {
            if (NotApplicable(probe, capabilities) is { } reason)
            {
                probes.Add(new ProbeReport(probe.Id, reason, ConformanceChecks.Skipped(probe, reason)));
                continue;
            }

            if (fixture?.Find(probe.Id) is { Skipped: { } fixtureReason })
            {
                var skipped = "The fixture skips it: " + fixtureReason;
                probes.Add(new ProbeReport(probe.Id, skipped, ConformanceChecks.Skipped(probe, skipped)));
                continue;
            }

            capture.Begin(probe);
            var result = await client.EvaluateAsync(probe.Request with { Model = target.Model }, cancellationToken).ConfigureAwait(false);
            var exchanges = capture.End();
            var outcome = new ProbeOutcome(probe, exchanges, result.IsSuccess ? result.Value : null, result.IsFailure ? result.Error : null);
            outcomes.Add(outcome);
            probes.Add(new ProbeReport(probe.Id, null, ConformanceChecks.Run(outcome, target)));
            if (mode == ConformanceMode.Record && target.Wire == ConformanceWire.ChatCompletions)
            {
                RecordChat(recorded, probe, exchanges, result.IsFailure ? result.Error : null);
            }
            else if (mode == ConformanceMode.Record)
            {
                recorded.Add(exchanges.Count > 0 && exchanges[^1].KeySwapped
                    ? exchanges[^1]
                    : exchanges.Count > 0
                    ? new ConformanceExchange { Probe = probe.Id, Skipped = "No request carried a key to make invalid: the client sent no Authorization or x-api-key." }
                    : new ConformanceExchange { Probe = probe.Id, Skipped = "No request was sent: " + result.Error.Kind.ToString() + ", " + result.Error.Message });
            }
        }

        if (mode == ConformanceMode.Record)
        {
            new ConformanceFixture
            {
                Target = target.Name,
                Source = ConformanceFixture.RecordedSource,
                Model = FirstModel(outcomes) ?? target.Model,
                Recorded = _time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Exchanges = recorded,
            }.Save(fixturePath!);
        }

        return new ConformanceReport
        {
            Target = target.Name,
            Wire = target.Wire,
            Model = target.Model,
            ModelRule = target.ModelRule?.Description,
            Mode = mode,
            FixtureSource = fixture?.Source,
            FixtureRecorded = fixture?.Recorded,
            Probes = probes,
            RunChecks = [ConformanceChecks.E4(outcomes)],
        };
    }

    /// <summary>Why a probe does not apply to these capabilities, or <see langword="null"/> when it does.</summary>
    internal static string? NotApplicable(ConformanceProbe probe, DecisionCapabilities? capabilities)
    {
        if (capabilities is null)
        {
            return null;
        }

        var questions = probe.Definition.Questions;
        if (capabilities.MaxQuestions is { } maxQuestions && questions.Count > maxQuestions)
        {
            return string.Create(CultureInfo.InvariantCulture, $"The target takes at most {maxQuestions} questions.");
        }

        foreach (var question in questions)
        {
            var kind = question.Kind switch
            {
                QuestionKind.Noul => QuestionKinds.Noul,
                QuestionKind.Choice => QuestionKinds.Choice,
                _ => QuestionKinds.Score,
            };
            if (capabilities.QuestionKinds is { } kinds && (kinds & kind) == QuestionKinds.None)
            {
                return "The target does not accept " + question.Kind.ToString() + " questions.";
            }

            if (question.Kind == QuestionKind.Choice && capabilities.MaxChoiceOptions is { } maxOptions && question.Options.Count > maxOptions)
            {
                return string.Create(CultureInfo.InvariantCulture, $"The target takes at most {maxOptions} options per Choice.");
            }
        }

        return null;
    }

    // A chat-completions probe sends one request per question, so every exchange is kept. The invalid probe is answered
    // with Validation before anything is sent, so it keeps none, and a replay checks that again.
    private static void RecordChat(List<ConformanceExchange> recorded, ConformanceProbe probe, IReadOnlyList<ConformanceExchange> exchanges, DecisionError? error)
    {
        if (exchanges.Count == 0)
        {
            if (probe.Kind != ProbeKind.Invalid)
            {
                recorded.Add(new ConformanceExchange { Probe = probe.Id, Skipped = "No request was sent: " + error?.Kind.ToString() + ", " + error?.Message });
            }

            return;
        }

        foreach (var exchange in exchanges)
        {
            if (!exchange.KeySwapped)
            {
                recorded.Add(new ConformanceExchange { Probe = probe.Id, Skipped = "No request carried a key to make invalid: the client sent no Authorization or x-api-key." });
                return;
            }
        }

        recorded.AddRange(exchanges);
    }

    // Replay answers by probe id, and on the chat-completions wire also by body: an id the suite does not know, a repeated
    // one on another wire, or a missing one fails the run instead of being skipped. A chat-completions invalid probe sends
    // nothing, so it needs no exchange.
    private static void CheckCovers(ConformanceFixture fixture, DecisionCapabilities? capabilities, ConformanceWire wire)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        foreach (var probe in ConformanceProbe.All)
        {
            known.Add(probe.Id);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        var repeated = new List<string>();
        foreach (var exchange in fixture.Exchanges)
        {
            if (!known.Contains(exchange.Probe))
            {
                unknown.Add(exchange.Probe);
            }
            else if (!seen.Add(exchange.Probe) && wire != ConformanceWire.ChatCompletions)
            {
                repeated.Add(exchange.Probe);
            }
        }

        var missing = new List<string>();
        foreach (var probe in ConformanceProbe.All)
        {
            if (NotApplicable(probe, capabilities) is null
                && !(wire == ConformanceWire.ChatCompletions && probe.Kind == ProbeKind.Invalid)
                && (fixture.Find(probe.Id) is not { } exchange || (exchange.Skipped is null && exchange.Status is null)))
            {
                missing.Add(probe.Id);
            }
        }

        if (unknown.Count > 0)
        {
            throw new InvalidDataException("The fixture names probes the suite does not have: " + string.Join(", ", unknown) + ".");
        }

        if (repeated.Count > 0)
        {
            throw new InvalidDataException("The fixture names these probes more than once: " + string.Join(", ", repeated) + ".");
        }

        if (missing.Count > 0)
        {
            throw new InvalidDataException("The fixture has no exchange for these probes: " + string.Join(", ", missing) + ".");
        }
    }

    private static string? FirstModel(List<ProbeOutcome> outcomes)
    {
        foreach (var outcome in outcomes)
        {
            if (outcome.Response?.Model is { Length: > 0 } model)
            {
                return model;
            }
        }

        return null;
    }
}
