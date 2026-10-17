---
id: conformance
title: Conformance
sidebar_position: 18
description: The suite that checks any decision endpoint's request and response shapes, probabilities, errors, request ids and models, how to run it against your own endpoint, and where its fixtures come from.
---

# Conformance

Minos reaches several providers, and each one should answer the same request the same way. The conformance suite,
`Minos.NET.Conformance`, checks that. It sends a fixed set of probes to an endpoint through an `IDecisionClient`, reads
each answer twice, once as the wire sent it and once as Minos read it, and runs its checks on both. It works against any
endpoint a Minos client can reach, a server of your own included.

The suite runs on every CI build, against the TypeSafe, OpenRouter and OpenAI fixtures described below. It is not
published to NuGet on its own. The `minos` tool bundles it: `minos conformance` runs every probe live against a provider
from your `minos.json`, and writes `conformance.json` and `conformance.md`. The `bad-key` probe is skipped when no key is
configured. See [Running it from the command line](datasets.md#running-it-from-the-command-line).

## The probes

Each probe is one request with a stable id. The probes ask only what every provider can express, so none sets a Noul's
`WhenTrue` or a criterion's examples, which OpenAI cannot send.

| Probe | What it sends | What it expects |
| --- | --- | --- |
| `noul` | One Noul question, `is_urgent`. | Answers. |
| `choice` | One Choice question, `department`, with two options. | Answers. |
| `score` | One Score question, `frustration`, with three levels. | Answers. |
| `mixed` | The three in one set, keyed `is_urgent`, `department` and `frustration`, which is not alphabetical. | Answers. |
| `keyed` | A set built with `QuestionSetBuilder`: a Choice with the options `pro-plan` and `team-plan`, and a Score. | Answers. |
| `invalid` | A request with no questions. | A 400 or a 422. |
| `bad-key` | The `noul` request with an invalid key. | A 401 or a 403. |

A probe that the client's [capabilities](providers.md#capabilities) rule out, such as `choice` on a Noul-only
server, is skipped with the reason, and so are its checks.

## The checks

Each check passes, fails or is skipped. A check that cannot run is skipped, never passed. A sum or an expected level may
be off by 0.01.

| Check | What it checks | Skipped when |
| --- | --- | --- |
| `S1` | The response is a JSON object with exactly one answer per question asked. | No request was sent. |
| `S2` | Each answer's `type` fits its question: a Noul is `noul` on System One and `predicate` on OpenAI; a Choice and a Score are `choice` and `score` on both. | No answer could be read; `S1` reports it. |
| `S3` | Minos reads the response without an error, and its result reads every key with its kind. | Never. |
| `D1` | Every probability is a finite number from 0 to 1. | No answer could be read; `S1` reports it. |
| `D2` | A Choice's or a Score's probabilities sum to 1 and cover exactly the options or levels sent. | The probe asks no Choice or Score, or no answer could be read. |
| `D3` | A Score's `score` is the sum of each level times its probability, where OpenAI gives each level in a probability's `value`. | The probe asks no Score, or no answer could be read. |
| `D4` | A reported confidence is from 0 to 1. | No answer could be read, or none carries a `confidence`. |
| `M1` | The `mixed` answers are matched by key, so a provider or a parser that relies on their order fails. | Minos returned an error, no answer could be read, or Minos's answer count differs from the questions asked. |
| `E1` | `invalid` gets a 400 or a 422 with a JSON body, which Minos maps to `Validation` with a message. A JSON body that leaves `DecisionError.Detail` null fails, and the failure names Minos. | No request was sent. |
| `E2` | `bad-key` gets a 401 or a 403, which Minos maps to `Unauthorized`, with one request sent under the client's retry setting. | No request was sent, or the client sent no key in `Authorization` or `x-api-key`, so there was no key to make invalid. A keyless local server answers the probe like any other request. |
| `E3` | An error response's request-id header equals `DecisionError.RequestId`. | There is no error response, or it carries none, as OpenRouter's 400 and 401 do. |
| `E4` | A 429 met during the run has its `retry-after-ms` or `Retry-After` in `DecisionError.RetryAfter` and its request id read. | No 429 was met. One cannot be provoked, so the suite keeps no made-up 429. |
| `R1` | A successful response names a model, and `DecisionResponse.Model` is the same. | Minos returned an error, the body is not a JSON object, or the response names none and the target promises none. |
| `R2` | The reported model fits the requested one by the target's rule. | The target states no rule, Minos returned an error, no answer could be read, or the response names no model. |
| `R3` | The usage, when there is one, has whole, non-negative token counts. | Minos returned an error, the body is not a JSON object, the response reports no usage, or the usage has no token counts. |

The report states the model rule `R2` applied. TypeSafe answers an alias such as `jev-latest` with a versioned id of the
same family, such as `jev-1.13.0`. OpenRouter answers with a prefixed form, such as `~typesafe/jev-latest` or
`typesafe/jev-1.13-20260917`. `E3` and `E4` read the request-id headers in the order the client does;
[Request ids](client-and-errors.md#request-ids) lists which provider sends which.

## Running it against your endpoint

A `ConformanceTarget` names the endpoint, its wire format, the model to ask, the rule its reported model follows, and a
factory that builds the client over the runner's `HttpClient`. `ConformanceRunner.RunAsync` runs every probe in one of
three modes:

- `Live` sends real requests and saves nothing.
- `Record` sends real requests and saves the exchange of each probe, errors included, to a fixture file.
- `Replay` answers each probe from a fixture file by its id, once: the target must not retry, because a second request
  for a probe fails the run. A fixture that names a probe the suite does not have, names one twice, or
  lacks one fails the run rather than skipping the probe.

<!-- snippet: Conformance_Record -->
```cs
// Records every probe against a /v1/systemone server of your own, then writes the report next to the fixture.
public static async Task<ConformanceReport> RecordAsync(Uri server, string model, string fixturePath)
{
    var target = new ConformanceTarget(
        "my-server",
        ConformanceWire.SystemOne,
        model,
        new ModelRule(
            "The server reports the model it was asked for.",
            static (requested, reported) => string.Equals(requested, reported, StringComparison.Ordinal)),
        http =>
        {
            // No retries, so each probe is sent once.
            var options = new SystemOneDecisionClientOptions
            {
                Provider = SystemOneProvider.Custom,
                BaseAddress = server,
                Model = model,
                MaxRetries = 0,
            };
            SystemOneDecisionClient.ConfigureHttpClient(http, options);
            return new SystemOneDecisionClient(http, options);
        });

    var report = await new ConformanceRunner().RunAsync(target, ConformanceMode.Record, fixturePath);
    await File.WriteAllTextAsync(Path.ChangeExtension(fixturePath, ".md"), report.ToMarkdown());
    return report;
}
```
<!-- endSnippet -->

A recording sends at most one request per probe, seven in all, when the client does not retry. The runner sets no
retries itself, which is why the snippet sets `MaxRetries = 0`. A probe the client's capabilities rule out sends nothing,
and each request the provider accepts may be billed. The fixture keeps the request body, the status, the response body, its
media type and only these headers: `x-request-id`, `x-typesafe-request-id`, `request-id`, `retry-after` and
`retry-after-ms`. It never records your key or any other request header.

For `bad-key` the runner replaces the `Authorization` and `x-api-key` request headers with an invalid key, and stops the
probe unsent if a header it finds cannot be replaced. A target whose endpoint authenticates another way, such as in a
different header or a query string, still sends its own credential on `bad-key`, so its factory must not use a real key
for that.

The report comes as JSON, from `ToJson`, and as markdown, from `ToMarkdown`. It names the target, the model and its rule,
the mode, the fixture's source and date, and has one row per check.

## The fixtures

Each fixture says where its exchanges came from:

| Source | Meaning |
| --- | --- |
| `recorded` | Recorded from the live endpoint by a `Record` run. The fixture must name the model and the date. |
| `tests` | Built from the response fixtures of the client's own unit tests, for a target with no recording yet. |
| `guide` | Built from the provider's documentation. |

An exchange recorded apart from the rest carries its own date, and a fixture may skip a probe, with the reason, when no
exchange for it exists yet. In CI the suite must fail no check on any fixture, and each fixture must skip exactly the
checks its test expects, so a check that starts to skip fails the build.

The Live smoke workflow records TypeSafe's and OpenRouter's fixtures on `main` and uploads them as an artifact; they
reach the repository through a pull request. Both were first recorded on 2026-10-12. A recording keeps only the
allowlisted response headers, and drops from each body the account identifiers a provider echoes back, such as the
`user_id` in OpenRouter's error bodies.

### Why OpenAI's success fixtures come from the guide

There is no OpenAI key to record with yet. OpenAI's success fixtures are built from the example in its
[Decisions guide](https://developers.openai.com/api/docs/guides/decisions), as the adapter's own tests are, and are
labelled `guide`. That example names no model and no usage, so `R1`, `R2` and `R3` skip for OpenAI. The `bad-key`
exchange is real, because a 401 needs no key. [Issue #161](https://github.com/MarcelRoozekrans/Minos.NET/issues/161)
tracks recording the rest once a key exists.

## Next

- [Providers](providers.md): the providers the suite checks, and how they differ.
- [The client and its errors](client-and-errors.md): every kind of `DecisionError`.
- [Testing your code](testing-your-code.md): test your own code against canned answers instead of a live endpoint.
