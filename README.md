# ZeroAlloc.Jev

[![CI](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml/badge.svg)](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/actions/workflows/ci.yml)

Unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One model: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back — directly from TypeSafe or through OpenRouter.

> **Not affiliated with TypeSafe AI.** ZeroAlloc.Jev is a community project in the [ZeroAlloc](https://github.com/ZeroAlloc-Net) family. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** early development, not yet published to NuGet. See [the roadmap](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/docs/planning/ROADMAP.md).

## What works so far

- `JevClient` calls `POST /v1/systemone` and `GET /v1/models` on TypeSafe or OpenRouter and returns `Result<T, JevError>` for every outcome — HTTP errors, network failures, time-outs and unreadable responses included.
- `[JevQuestions]` source generator: declare questions as a C# type; the question JSON is emitted at compile time and answers parse into `Noul`, `Choice<TEnum>` and `Score<TEnum>`.
- Native AOT: no reflection, verified by an AOT smoke app in CI.

## Example

```csharp
using ZeroAlloc.Jev;

using var jev = new JevClient(new JevClientOptions { ApiKey = apiKey }); // or set TYPESAFE_API_KEY

var result = await jev.EvaluateAsync(new SystemOneRequest
{
    State = "Help! My payouts have been failing for 3 days.",
    Questions = new Dictionary<string, JevQuestion>
    {
        ["is_urgent"] = new NoulQuestion { Instructions = "Does this convey urgency?" },
    },
});

if (result.IsSuccess && result.Value.Answers["is_urgent"] is NoulAnswer urgent)
{
    Console.WriteLine($"P(urgent) = {urgent.Noul:P0}");
}
else if (result.IsFailure)
{
    Console.WriteLine($"{result.Error.Kind}: {result.Error.Message}");
}
```

For OpenRouter, set `Provider = JevProvider.OpenRouter` and an OpenRouter key (or `OPENROUTER_API_KEY`).

### Retries

Rate limiting (429), overload (503, 529), other server errors (5xx), request time-outs (408), network failures and client time-outs are retried with exponential backoff, honouring the server's `Retry-After`. Defaults follow TypeSafe's official SDKs: 2 retries, 500 ms initial backoff, jitter, and waits capped at 30 s. Tune them with `MaxRetries`, `InitialBackoff`, `MaxRetryDelay` and `Jitter` on `JevClientOptions`. Retrying a time-out, network failure or 5xx can process, and bill, a request twice; set `MaxRetries = 0` where that matters.

## License

[MIT](https://github.com/ZeroAlloc-Net/ZeroAlloc.Jev/blob/main/LICENSE)
