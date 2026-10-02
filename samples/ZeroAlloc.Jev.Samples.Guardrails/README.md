# Guardrails sample

This sample screens chat messages before they reach an assistant's language model. It shows three things:

- **Screening in front of the model.** Each incoming message is checked first, so a hostile or sensitive one can be stopped or sent to a person before any generation happens.
- **One request, five questions.** Every message costs a single Jev request. It asks four yes/no questions (does the message override the instructions, share personal data, ask for medical or legal advice, or abuse someone) and one harm score.
- **Two policies over the same answers.** A Strict policy, for a public assistant, and a Lenient one, for an internal tool, each turn the answers into Allow, Review or Block. No second call is made to compare them.

The twelve messages are written for the sample and range from a parcel question to a jailbreak attempt.

## Run it

```
dotnet run --project samples/ZeroAlloc.Jev.Samples.Guardrails
```

By default the sample replays the answers checked in as `recordings.json`. It needs no network and no key.

Replay and record read and write the recordings in your clone of the repository, so run those two modes from a clone. `--live` reads no recordings and runs from anywhere.

To call the API instead, set `OPENROUTER_API_KEY` and pass a mode after `--`:

- `-- --live` sends the requests and prints the report.
- `-- --record` does the same and then rewrites `recordings.json` from the responses. It writes straight to the sample's source folder, and replay reads the same file, so a fresh recording shows on the next run.

The live modes make 12 billed requests.

## The recordings

`recordings.json` was recorded through OpenRouter with the model `typesafe/jev-1.13-20260917` on 2026-10-02. Record again whenever the questions or the messages change.

## Tuning

The thresholds in `GuardrailPolicy.cs` are starting points, not recommendations. Tune them on your own traffic and decide what a review means in your product.

A request for medical or legal advice is only ever reviewed, never blocked on that answer alone. The question says what a message is about, not that it is harmful, so it is routed to a person; it is blocked only when its content is harmful, which the severity score decides.

On the recorded messages both policies give the same split, because the model is confident about every one of them. The two only part ways on borderline answers. The policy tests in `tests/ZeroAlloc.Jev.Samples.Tests` feed made-up answers through each rule and show where their thresholds differ.

## Credit

Inspired by TypeSafe's [Guardrails for LLMs](https://docs.typesafe.ai/cookbooks/llm_guardrails) cookbook; the code, messages and policies here are our own.
