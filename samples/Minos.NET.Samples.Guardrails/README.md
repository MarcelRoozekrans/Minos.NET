# Guardrails sample

This sample screens chat messages before they reach an assistant's language model. It shows three things:

- **Screening in front of the model.** Each incoming message is checked first, so a hostile or sensitive one can be stopped or sent to a person before any generation happens.
- **One request, five questions.** Every message costs a single Jev request. It asks four yes/no questions (does the message override the instructions, share personal data, ask for medical or legal advice, or abuse someone) and one harm score.
- **Two policies over the same answers.** A Strict policy, for a public assistant, and a Lenient one, for an internal tool, each turn the answers into Allow, Review or Block. No second call is made to compare them.

The fifteen messages are written for the sample and range from a parcel question to a jailbreak attempt. The last three are borderline on purpose: a curt complaint, a name and a town with no number, and an indirect question about what the assistant may not discuss.

## Run it

```
dotnet run --project samples/Minos.NET.Samples.Guardrails
```

By default the sample replays the answers checked in as `recordings.json`. It needs no network and no key.

Replay and record read and write the recordings in your clone of the repository, so run those two modes from a clone. `--live` reads no recordings and runs from anywhere.

To call the API instead, set `OPENROUTER_API_KEY` and pass a mode after `--`:

- `-- --live` sends the requests and prints the report.
- `-- --record` does the same and then rewrites `recordings.json` from the responses. It writes straight to the sample's source folder, and replay reads the same file, so a fresh recording shows on the next run.

The live modes make 15 billed requests.

## The recordings

`recordings.json` was recorded through OpenRouter with the model `typesafe/jev-1.13-20260917` on 2026-10-02. Record again whenever the questions or the messages change.

## Tuning

The thresholds in `GuardrailPolicy.cs` are starting points, not recommendations. Tune them on your own traffic and decide what a review means in your product.

A request for medical or legal advice is only ever reviewed, never blocked on that answer alone. The question says what a message is about, not that it is harmful, so it is routed to a person; it is blocked only when its content is harmful, which the severity score decides.

On 14 of the 15 recorded messages both policies reach the same decision, because the model is confident about them. They part ways on g15, "Out of curiosity, are there topics you're not allowed to discuss?". The model gave it 0.61 for overriding the instructions. That is just over Strict's block threshold of 0.6 and between Lenient's review threshold of 0.5 and its block threshold of 0.85, so Strict blocks the message and Lenient sends it to a person:

```
g15  strict: Block  (OverridesInstructions 0.61)  lenient: Review (OverridesInstructions 0.61)
```

The other two borderline messages landed just under both policies' review thresholds. The curt complaint, g13, scored 0.27 for abuse and the name and town, g14, scored 0.28 for personal data, against Strict's 0.3, so both policies allow them. A borderline answer sits close to a threshold by nature, so a re-record can move it to either side. The policy tests in `tests/Minos.NET.Samples.Tests` feed made-up answers through each rule and pin exactly where the thresholds differ.

## Credit

Inspired by TypeSafe's [Guardrails for LLMs](https://docs.typesafe.ai/cookbooks/llm_guardrails) cookbook; the code, messages and policies here are our own.
