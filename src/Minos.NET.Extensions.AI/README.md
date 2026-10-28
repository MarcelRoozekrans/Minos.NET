# Minos.NET.Extensions.AI

An `IDecisionClient` for [Minos.NET](https://www.nuget.org/packages/Minos.NET) over any Microsoft.Extensions.AI `IChatClient`: OpenAI, OpenRouter, Azure, Ollama or any other chat model answers Noul, Choice and Score questions. Each answer says how its probabilities were made: from the model's logprobs, or verbalized, as the model's own estimate. The bridge does not dispose the chat client. Native AOT-compatible.

```
dotnet add package Minos.NET.Extensions.AI
```

```csharp
using Microsoft.Extensions.AI;
using Minos;
using Minos.Extensions.AI;

// chatClient is any IChatClient, such as an OpenAI ChatClient wrapped with AsIChatClient().
using var client = chatClient.AsDecisionClient(new ChatDecisionClientOptions { ProbabilityMode = ProbabilityMode.Auto });
var answers = await client.EvaluateAsync<Triage>(message);
```
