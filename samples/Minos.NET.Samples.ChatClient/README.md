# Chat-client sample

This sample asks a Microsoft.Extensions.AI `IChatClient` two questions about each of three support tickets, through `Minos.NET.Extensions.AI`. It shows three things:

- **Any chat client is a decision client.** `AddChatDecisionClient` turns the `IChatClient` in the container into an `IDecisionClient`, so the same `[Questions]` record that works with TypeSafe works with OpenAI, OpenRouter, Azure or Ollama.
- **Every answer says where its probabilities came from.** `ProbabilitySource` is `Logprobs` when the model returned logprobs for its first token, and `Verbalized` when the model had to state the probabilities as JSON. The first ticket shows the first, the other two show the second.
- **Recording chains on.** `.UseRecording()` records each decision with its sources. The sample keeps the records in memory and prints which source each answer had.

The default `ProbabilityMode.Auto` asks for logprobs first and asks again, verbalized, when the reply has none. That is what the other two tickets show.

## Run it

```
dotnet run --project samples/Minos.NET.Samples.ChatClient
```

Without a key the sample runs offline. A fake chat client plays the model: it gives logprobs for the first ticket and plain JSON for the rest. It needs no network.

To ask a real model, set `OPENROUTER_API_KEY`. The sample then calls OpenRouter through the OpenAI client, with the model `openai/gpt-4.1-mini`. Pass another model after `--`:

```
dotnet run --project samples/Minos.NET.Samples.ChatClient -- --model openai/gpt-4.1
```

The online mode makes billed requests: one per question per ticket, 6 in all, plus one more for each question whose model returns no logprobs. A model that returns no logprobs shows `Verbalized` for every answer.

## Things to know

There are no recordings to replay. The offline run is covered by a test that compares its output with a snapshot.

The fake chat client is the only part that is not what you would write in an app. In an app you register your own `IChatClient` and call `AddChatDecisionClient()`.
