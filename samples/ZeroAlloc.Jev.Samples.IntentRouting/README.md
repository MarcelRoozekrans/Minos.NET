# Intent-routing sample

This sample is a travel-booking assistant that decides who handles each incoming request. It shows three things:

- **Jev as the cheap first step.** One Jev request per message asks what the traveller wants and whether they travel within the next 24 hours. Only that small answer is needed to pick a route.
- **Three destinations.** A plain lookup is answered by code, with no language model. A change to a booking goes to an assistant model that has the booking as context. Disputes, unclear requests and anything without a flow go to a person.
- **Confidence decides when to hand over.** If the model is unsure what the traveller wants, the request goes to a person whatever the intent says. The urgency flag is separate and is read from the second question.

Only changes reach a language model, so the more of the traffic that goes to code or a person, whether lookups, disputes or other requests, the less generation you pay for. In the recorded run 8 of the 12 requests needed no language model.

The twelve requests are written for the sample and range from a lookup to the keyboard noise `asdf`.

## Run it

```
dotnet run --project samples/ZeroAlloc.Jev.Samples.IntentRouting
```

By default the sample replays the answers checked in as `recordings.json`. It needs no network and no key.

To call the API instead, set `OPENROUTER_API_KEY` and pass a mode after `--`:

- `-- --live` sends the requests and prints the report.
- `-- --record` does the same and then rewrites `recordings.json` from the responses. It writes straight to the sample's source folder, and replay reads the same file, so a fresh recording shows on the next run.

The live modes make 12 billed requests.

## The recordings

`recordings.json` was recorded through OpenRouter with the model `typesafe/jev-1.13-20260917` on 2026-10-02. Record again whenever the questions or the requests change.

## Things to know

The model answered the keyboard noise `asdf` with high confidence as Other, so it went to a person through the Other route and not through the low-confidence rule. Do not count on low confidence to catch nonsense input.

The 24-hour question is asked for every request, including those that go to a person, so a human can see the urgency at a glance.

## Credit

Inspired by TypeSafe's [intent routing](https://docs.typesafe.ai/patterns/intent-routing) pattern and its [Function calling](https://docs.typesafe.ai/cookbooks/function_calling) cookbook; the code and requests here are our own.
