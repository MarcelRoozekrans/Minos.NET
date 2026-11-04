---
id: running-locally
title: Running decisions locally and privately
sidebar_position: 14
description: Move an app from a hosted provider to a local decision server. Record its decisions, shadow the local model, read the shadow report, and switch by configuration.
---

# Running decisions locally and privately

A local decision server lets an app decide offline, and keeps its state on the machine. This page takes one small app
from a hosted provider to a local server, and shows how to learn from the app's own traffic whether the local model
decides well enough before you switch.

The reason to go local is offline use and privacy, not cost. In the Banking77 run described in
[step 5](#step-5-decide), the hosted provider cost 0.0293118 per 1,000 decisions, in the currency of the prices, the report's own figure.

The path has six steps:

1. [Record](#step-1-record) every decision, with redaction.
2. [Shadow the local server](#step-2-shadow-the-local-server) behind the hosted provider.
3. [Attach outcomes](#step-3-attach-outcomes) when the right answer becomes known.
4. [Export and report](#step-4-export-and-report) with the tool.
5. [Decide](#step-5-decide) from the shadow report.
6. [Switch](#step-6-switch) by configuration.

[What stays private, step by step](#what-stays-private-step-by-step) says which provider sees the state at each step.
[When no state may leave at all](#when-no-state-may-leave-at-all) covers an app that cannot shadow behind a hosted
provider.

This page shows the path and links to the reference pages for options, edge cases and formats.

## Before you start

You need three packages, and a local server:

```shell
dotnet add package Minos.NET.SystemOne
dotnet add package Minos.NET.Recording
dotnet tool install --global Minos.NET.Tool
```

The guide needs a Minos.NET.Recording version that has `UseShadowRecording`, a method that registers the shadow stage
once the container has built the candidate. [Shadow mode](recording.md#shadow-mode) describes it. The commands need
Minos.NET.Tool 1.8.0 or later.

The local server in this guide is `ollajev` serving the decision model `Mapika/decider-2b-GGUF:Q4_K_M` on port 8000:

```shell
ollajev serve Mapika/decider-2b-GGUF:Q4_K_M --port 8000 --no-browser
```

[Running a local server](providers.md#running-a-local-server) has the install and pull commands, and the warning never
to pass `--trust`.

## The example app

The app is a background job that reviews customer payment messages, one decision per message. Each decision asks two
questions: a Noul question, whether the customer disputes a charge, and a Choice question, what the message is about.
The Banking77 report measures the same two kinds.

<!-- snippet: RunningLocally_Questions -->
```cs
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minos;
using Minos.Recording;

public enum PaymentTopic
{
    [Criterion("Asks for money back")]
    Refund,

    [Criterion("A charge the customer says they did not make")]
    Fraud,

    [Criterion("Charged more than once for the same purchase")]
    Duplicate,
}

[Questions]
public partial record PaymentReview
{
    [Noul("Does the customer dispute a charge?")]
    public partial Noul IsDisputed { get; }

    [Choice("What is the message about?")]
    public partial Choice<PaymentTopic> Topic { get; }
}
```
<!-- endSnippet -->

The configuration holds a hosted section and a `Local` section that uses the `Custom` preset. `Minos:Active` names the
hosted one, as in [Switching providers](providers.md#switching-providers). `Minos:Shadow` is a setting this app reads, not one the library reads: it names the section of the
candidate that answers the same decisions in the background.

```json
{
  "Minos": {
    "Active": "TypeSafe",
    "Shadow": "Local",
    "Providers": {
      "TypeSafe": { "Provider": "TypeSafe" },
      "Local": {
        "Provider": "Custom",
        "BaseAddress": "http://localhost:8000/",
        "Model": "Mapika/decider-2b-GGUF:Q4_K_M"
      }
    }
  }
}
```

## Step 1: record

Register one `DecisionRecorder` over the app's sink, and put the recording stage outermost on the active client, so it
records what the caller gets. `RecordingOptions.Redact` runs before a record is kept. Here it keeps text with e-mail
addresses masked, and drops a decision on structured state altogether, since `RedactionResult.Drop` keeps no record
of it. The providers still receive the state whole.

With no `Minos:Shadow` set, the app's registration only records. The setting is the app's own, read by this snippet.

<!-- snippet: RunningLocally_Register -->
```cs
public static partial class LocalReadiness
{
    // Minos:Active names the provider section that decides. Minos:Shadow, when set, names the section of a candidate
    // that answers the same decisions in the background; unset, the app records only.
    public static IHostApplicationBuilder AddRecordedDecisions(this IHostApplicationBuilder builder, IDecisionRecordSink sink)
    {
        var configuration = builder.Configuration;
        var active = ProviderSection(configuration, "Minos:Active");

        // One recorder over the app's sink. Text is kept with e-mail addresses masked; the providers still get it whole.
        // A decision on structured state is not recorded at all, since Drop keeps no record of it.
        builder.Services.AddSingleton(provider => new DecisionRecorder(
            sink,
            new RecordingOptions
            {
                Redact = state => state.TryGetString(out var text)
                    ? RedactionResult.Keep(Email().Replace(text!, "[email]"))
                    : RedactionResult.Drop,
            },
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()));
        builder.Services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // The stage added to the registration wraps the client and its standard pipeline, so recording is outermost.
        var client = builder.Services.AddSystemOneDecisionClient(active);
        if (string.IsNullOrWhiteSpace(configuration["Minos:Shadow"]))
        {
            client.UseRecording();
            return builder;
        }

        var shadow = ProviderSection(configuration, "Minos:Shadow");
        if (string.Equals(shadow.Key, active.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Minos:Shadow names {shadow.Key}, which is already Minos:Active: shadow another provider.");
        }

        // The candidate is a client of its own, keyed by its section's name, with no recording stage. On a CPU it takes
        // seconds per decision, so one call at a time, and a time-out well above that. The cap bounds the load.
        builder.Services.AddSystemOneDecisionClient(shadow.Key, shadow);
        // One ShadowOptions per candidate, keyed like it, so every pipeline that shadows it shares one daily cap.
        builder.Services.AddKeyedSingleton(shadow.Key, (provider, _) => new ShadowOptions(provider.GetRequiredKeyedService<IDecisionClient>(shadow.Key), dailyCap: 2000)
        {
            MaxConcurrent = 1,
            Timeout = TimeSpan.FromSeconds(30),
        });

        // The candidate exists only once the container builds it, so the container creates the stage: it calls the
        // factory when it builds the active client, and gives the stage its DecisionRecorder.
        client.UseShadowRecording(provider => provider.GetRequiredKeyedService<ShadowOptions>(shadow.Key));
        return builder;
    }

    private static IConfigurationSection ProviderSection(IConfiguration configuration, string setting)
    {
        var name = configuration[setting];
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Set {setting} to a section under Minos:Providers.");
        }

        var section = configuration.GetSection("Minos:Providers").GetSection(name);
        return section.Exists()
            ? section
            : throw new InvalidOperationException($"{setting} names {name}, but Minos:Providers has no section named {name}.");
    }

    [GeneratedRegex(@"[^\s@]+@[^\s@]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Email();
}
```
<!-- endSnippet -->

The snippet takes its sink as a parameter. The JSONL sink is for development and tests; a production app writes a sink
over its own store, as [Writing your own sink](recording.md#writing-your-own-sink) shows.

The app makes each decision inside a recording scope and keeps the decision id, which it needs in step 3. The customer
id is the erasure key, so a request to forget the customer reaches this decision.

<!-- snippet: RunningLocally_Review -->
```cs
public static class PaymentReviews
{
    // The customer id is the erasure key, so a request to forget the customer reaches this decision.
    public static async Task<(PaymentReview? Review, string? DecisionId)> ReviewAsync(
        IDecisionClient client, string customerId, string text, CancellationToken cancellationToken)
    {
        using var scope = DecisionRecording.Begin(erasureKey: customerId);
        var result = await client.EvaluateAsync<PaymentReview>(text, cancellationToken);

        // Keep the id with the payment, so the desk's answer can be attached to this decision later.
        return (result.IsSuccess ? result.Value : null, scope.DecisionId);
    }
}
```
<!-- endSnippet -->

## Step 2: shadow the local server

With `Minos:Shadow` set, the app's registration builds the local client from the `Local` section and passes it as the
candidate in `ShadowOptions`. The settings come from the measured run. On a CPU the candidate took a median of 4043.8
ms and a p95 of 4509.7 ms per decision, so `MaxConcurrent` is 1, one call at a time, and `Timeout` is 30 seconds, well
above the p95.

The daily cap is required. For a free local candidate it bounds the load on the machine, not a bill. The candidate is a keyed client, which exists
only once the container builds, so `UseShadowRecording` takes a factory that reads the options from the container.
`ShadowOptions` is a keyed singleton, so every
pipeline that shadows this candidate shares one cap.

The candidate runs in the background and never changes or delays the primary's result. A decision whose record is dropped, by redaction or a full queue, is never shadowed, since there is no record to hold the answers. Its answers are written into the
decision's record. [Shadow mode](recording.md#shadow-mode) covers when the candidate runs, what it is sent and how
the second write works.

Naming the active section as the shadow, or a section that does not exist, throws an `InvalidOperationException` when
the registration runs.

## Step 3: attach outcomes

When the right answer to a decision becomes known, attach it by the decision id. The payments desk settles the case,
and its answers are the ones both the primary and the candidate are measured on.

<!-- snippet: RunningLocally_Outcomes -->
```cs
public static class PaymentOutcomes
{
    // The payments desk settled the case: the right answers, which both the primary and the candidate are measured on.
    public static ValueTask ConfirmAsync(
        IDecisionRecorder recorder, string decisionId, bool disputed, PaymentTopic topic, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Confirmed("payments-desk", DateTimeOffset.UtcNow, new Dictionary<string, ConfirmedAnswer>
            {
                ["is_disputed"] = ConfirmedAnswer.ForNoul(disputed),
                ["topic"] = ConfirmedAnswer.ForChoice(TopicKey(topic)),
            }),
            cancellationToken);

    // The option keys the question set sends for each topic.
    private static string TopicKey(PaymentTopic topic) => topic switch
    {
        PaymentTopic.Refund => "refund",
        PaymentTopic.Fraud => "fraud",
        PaymentTopic.Duplicate => "duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };
}
```
<!-- endSnippet -->

Something that happened afterwards, such as a customer who wrote back, is a signal, not an answer. A signal never
becomes an expected answer. [Outcomes](recording.md#outcomes) explains why and shows both.

## Step 4: export and report

Two commands read the record file. Neither calls a provider.

```shell
minos shadow-report --records decisions.jsonl --prices prices.json --out report
minos export --records decisions.jsonl --out export
```

`minos shadow-report` writes `shadow-report.json` and `shadow-report.md` into the `--out` directory. It compares the
candidate's answers with the primary's on the decisions that were shadowed, and `--prices` supplies the per-token
prices for the cost lines. The report already holds each side's reliability bins and threshold sweep, which is enough
to re-tune a threshold for the local model. See [From the command line](recording.md#from-the-command-line).

`minos export` turns the records that have confirmed outcomes into a labelled dataset. Use it to run `minos calibrate`
again later, on the local model alone or on a new candidate, against the same confirmed outcomes. See
[Exporting recorded decisions](datasets.md#exporting-recorded-decisions).

The two commands write copies of recorded state, so keep their output as private as the record file.

## Step 5: decide

The Banking77 shadow report shows how to read one. The run replayed 400 Banking77 decisions through TypeSafe as the
primary, with the local `decider-2b` server as the candidate, on a CPU-only runner. The full report is
[committed to the repository](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/docs/reports/shadow/banking77/shadow-report.md).
Apply the same checklist to your own report.

| Question | What to read | Banking77 answer |
| --- | --- | --- |
| Right as often? | Accuracy against the confirmed outcomes, over the same 1200 questions | Candidate 0.9267, primary 0.9275 overall |
| Same answers? | Agreement per question, and the confusion table | Intent 0.9600, `card_not_arrived` 0.9300, `about_a_payment` 0.9450 |
| Can its confidence be trusted? | ECE, the expected calibration error | Candidate 0.0510, primary 0.0262 |
| Fast enough? | Median and p95 latency | Candidate median 4043.8 ms and p95 4509.7 ms, primary median 50.5 ms |
| Cost and privacy? | Cost per 1,000 decisions, and where the state goes | No per-token price for the local model, and with it as primary no state leaves the machine. In this run TypeSafe was the primary and got every state |

Reading each row:

- **Right as often?** Overall the two are level. By kind the candidate is ahead on the Noul questions, 0.9363 against
  0.9287, and behind on the ten-way Choice, 0.9075 against 0.9250. The 400 decisions give no tight interval, so a gap
  this small is not a ranking.
- **Same answers?** Agreement says how often the two give the same answer, whether or not it is right. The confusion
  table in the report shows which intents swap.
- **Can its confidence be trusted?** The candidate's ECE is about twice the primary's. A threshold tuned on the hosted
  provider does not carry over: tune it again on the local model's own reliability table, by the method in
  [Choosing a threshold](choosing-a-provider.md#choosing-a-threshold).
- **Fast enough?** The candidate answered in a median of 4043.8 ms against 50.5 ms. That fits a background job, not a
  request a person waits on.
- **Cost and privacy.** The hosted provider cost 0.0293118 per 1,000 decisions, in the currency of the prices. The local model has no per-token price,
  and the state stays on the machine.

**The reading for Banking77.** Overall the local model is about as accurate as TypeSafe on this set, a little better on
the yes-or-no questions and a little worse on the intent choice. It is good enough where its latency fits, once the
thresholds are checked on its own reliability table. That holds for this dataset and this hardware, and says nothing
about another workload.

**What replayed traffic does not show.** The items are the dataset's, sent in a loop, not production traffic. Run shadow
mode on your own traffic before you switch.

## Step 6: switch

Set `Minos:Active` to `Local`, in the file or with the environment variable `Minos__Active`. No code changes. The
second configuration block is the same app after the switch, with no `Minos:Shadow`:

```json
{
  "Minos": {
    "Active": "Local",
    "Providers": {
      "TypeSafe": { "Provider": "TypeSafe" },
      "Local": {
        "Provider": "Custom",
        "BaseAddress": "http://localhost:8000/",
        "Model": "Mapika/decider-2b-GGUF:Q4_K_M"
      }
    }
  }
}
```

Remove the shadow candidate, or keep recording with no shadow, so outcomes keep arriving for later checks. Do not
shadow the hosted provider behind the local one: that sends the state off the machine again.

## What stays private, step by step

| Step | Who sees the state | Notes |
| --- | --- | --- |
| Record | The active provider | Redaction runs before a record is kept and changes only what is recorded. The provider still gets the state whole. |
| Shadow | The active provider, and the candidate | The candidate is sent the state unredacted, as the primary is. A local candidate keeps it on the machine. |
| Outcomes | Nobody new | An outcome is stored with the record, under the same erasure key. |
| Export and report | Nobody | Neither command calls a provider. Their files are copies of recorded state. |
| Switch | The local server only | No state leaves the machine, as long as no hosted candidate is shadowed behind it. |

Three more points hold across the steps:

- The client never writes the state to logs, traces or exception messages. [Logging, traces and
  metrics](observability.md) lists what it does emit.
- Erasure goes by the app's key. The JSONL sink's erasure is a tombstone that leaves the erased state in the file, so
  production needs a sink over its own store that deletes. See [Erasure](recording.md#erasure).
- An export is a copy that a later erasure does not reach. After an erasure, delete the earlier exports and export
  again.

## When no state may leave at all

Shadowing behind a hosted primary sends the state to the hosted provider. An app whose state may go only to a
zero-data-retention account, or nowhere off the machine, cannot shadow on that traffic behind a provider that does not
meet the rule.

For that app, make the local server the primary from the start. Record with confirmed outcomes, then run `minos export`
and `minos calibrate` to measure the local model against those outcomes. Or run a shadow on traffic that is allowed to
go to a hosted provider. [Privacy: zero data retention](providers.md#privacy-zero-data-retention) covers the hosted
setting.

## Next

- [Recording decisions](recording.md): the recorder, outcomes, erasure, redaction, sinks and shadow mode in full.
- [Labelled datasets](datasets.md): the export format, and how a dataset is run with `minos calibrate`.
- [Providers](providers.md): the presets, switching providers and running a local server.
- [Choosing a provider and a threshold](choosing-a-provider.md): how to pick the confidence threshold.
- [Logging, traces and metrics](observability.md): what Minos emits and never emits.
- [The Banking77 shadow report](https://github.com/MarcelRoozekrans/Minos.NET/tree/main/docs/reports/shadow/banking77): the report this page reads, with its records.
