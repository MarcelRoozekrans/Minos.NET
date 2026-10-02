# Samples

Three cookbook samples show Jev doing real work in a small console app. Each one has its own README with the details.

| Sample | What it shows |
| --- | --- |
| [Guardrails](ZeroAlloc.Jev.Samples.Guardrails) | Screens chat messages before they reach a language model, with one request per message and two policies over the same answers. Inspired by TypeSafe's [Guardrails for LLMs](https://docs.typesafe.ai/cookbooks/llm_guardrails) cookbook. |
| [Intent routing](ZeroAlloc.Jev.Samples.IntentRouting) | Decides per request whether code, an assistant model or a person handles it, using Jev as the cheap first step. Inspired by TypeSafe's [intent routing](https://docs.typesafe.ai/patterns/intent-routing) pattern and its [Function calling](https://docs.typesafe.ai/cookbooks/function_calling) cookbook. |
| [Re-ranking](ZeroAlloc.Jev.Samples.Reranking) | Re-orders a keyword shortlist of help articles by how well each answers the question, in one fan-out request. Inspired by TypeSafe's [Re-ranking](https://docs.typesafe.ai/cookbooks/rerank_typesafe) cookbook. |

Every sample is our own work. Each is inspired by TypeSafe's material, linked in the table above, but the code, the data and the policies are written here.

## Run a sample

```
dotnet run --project samples/ZeroAlloc.Jev.Samples.Guardrails
```

Run the samples from a clone of the repository. Replay reads each sample's `recordings.json` from its source folder in the checkout, so it does not work from a copied build output.

A sample runs in one of three modes, chosen by the argument after `--`:

- **Replay** is the default and needs no network and no key. It answers every request from the checked-in `recordings.json`.
- `--live` calls the API and prints the report. It needs `OPENROUTER_API_KEY` and makes billed requests.
- `--record` does the same as `--live` and then rewrites the sample's `recordings.json` from the responses. It needs `OPENROUTER_API_KEY` as well.

## The recordings

The checked-in recordings are real OpenRouter answers from the model `typesafe/jev-1.13-20260917`, all recorded on 2026-10-02. A recording holds only the provider, the model, the date and the response bodies keyed by a hash of the request. It never holds headers or a key, and a test checks that. Record again with `--record` when a sample's questions or data change. A change to the client's request format in the core library changes the request of every sample, so it means re-recording all of them; replay names the sample and the command when a request has no recorded answer.

## Not a sample

`ZeroAlloc.Jev.AotSmoke` in this folder is the Native AOT smoke app that CI publishes and runs. It checks that the client survives trimming and AOT compilation, and it is not a cookbook sample.
