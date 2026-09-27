# Upstream gap analysis: ZeroAlloc-Net for Jev.Net

**Date:** 2026-09-24
**Scope:** What Jev.Net needs from ZeroAlloc-Net for milestones 1 and 3, checked against the latest released packages. Sources: `docs/planning/ROADMAP.md`, the follow-ups in the phase 1.1 and 1.2 plans, and https://docs.typesafe.ai/api.md and https://docs.typesafe.ai/models.md.
**Method:** Read the source at each release tag, then built throwaway probe projects against the released NuGet packages under the session scratchpad (`upstream-probe/a` through `upstream-probe/g`). All probes used `net10.0`, SDK 10.0.401, `IsAotCompatible=true` and `TreatWarningsAsErrors=true`.

## Versions inspected

| Repo | Latest release | NuGet packages used |
|---|---|---|
| ZeroAlloc.Rest | v1.3.5, 2026-09-24 | ZeroAlloc.Rest, .SystemTextJson, .Resilience 1.3.5. The generator ships inside ZeroAlloc.Rest |
| ZeroAlloc.Resilience | v1.3.4, 2026-09-20 | ZeroAlloc.Resilience 1.3.4. The generator ships inside it |
| ZeroAlloc.Results | v1.2.3, 2026-09-20 | ZeroAlloc.Results 1.2.3 |
| ZeroAlloc.Telemetry | v1.6.3, 2026-09-20 | ZeroAlloc.Telemetry 1.6.3 |
| ZeroAlloc.Inject | v1.7.6, 2026-09-20 | ZeroAlloc.Inject + ZeroAlloc.Inject.Generator 1.7.6 |
| ZeroAlloc.Validation | v1.7.5, 2026-09-21 | ZeroAlloc.Validation, .Generator, .Options 1.7.5 |
| ZeroAlloc.TestHelpers | v1.3.2, 2026-09-20 | ZeroAlloc.TestHelpers 1.3.2 |

ZeroAlloc.Serialisation 2.4.4 was not needed. Jev.Net serializes through STJ source generation, and the STJ path through ZeroAlloc.Rest.SystemTextJson works under AOT (see need 5).

Existing issues: every repo above was listed with `--state all` and searched with the keywords Retry-After, body, HttpRequestException, serializer, JsonTypeInfo, status code, accessibility and public. **No repo has an open issue that covers any gap below.** The only related closed issues are Rest #295, fixed in 1.3.5, and Validation #174. #174 is the same packaging defect as V1, but in a different package.

---

## 1. Summary

| # | Need | Upstream repo | Status | Evidence | Existing issue |
|---|---|---|---|---|---|
| 1 | `POST /v1/systemone` with a JSON body | ZeroAlloc.Rest | Supported | Probe a: `POST https://api.typesafe.ai/v1/systemone` sent, 200 deserialized | none needed |
| 2 | `GET /v1/models` | ZeroAlloc.Rest | Supported | Probe a: `ModelsAsync` sent `GET v1/models` and returned a failed Result for 529 | none needed |
| 3 | Base address plus `Authorization: Bearer` on every request | ZeroAlloc.Rest | Supported | `ZeroAllocClientOptions.BaseAddress`; generated `Add{I}` returns `IHttpClientBuilder`, `DiEmitter.cs:42` @ v1.3.5. Probe a saw `auth=Bearer k` on the wire | none needed |
| 4 | Client interface stays `internal` | ZeroAlloc.Rest | Supported in 1.3.5 | `ModelExtractor.cs:50-61`, `ClientModel.cs:13-18` @ v1.3.5. Probe a: `typeof(JevApiClient).IsPublic == False`, internal DTOs compile | #295, closed and fixed |
| 5 | Native AOT, zero IL2xxx/IL3xxx, STJ source-gen context | ZeroAlloc.Rest.SystemTextJson | Supported | `ClientEmitter.cs:140-141` puts `[UnconditionalSuppressMessage]` for IL2026/IL3050 on each generated method. Probe a: `dotnet build` and `dotnet publish -r win-x64` with PublishAot report 0 warnings under TWAE, and the native exe runs correctly | none needed |
| 6 | Jev.Net's serializer isolated from the host app's other ZeroAlloc.Rest clients | ZeroAlloc.Rest | **Missing** | The interface-level `[Serializer]` is read by `ModelExtractor.cs:39` but never used by either emitter. `DiEmitter.cs:38-39` does `TryAddSingleton` of the shared `IRestSerializer`. Probe g below | none. Draft **R4**, filed as [ZeroAlloc.Rest#301](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/301) |
| 7 | Result error carries the status code and the `Retry-After` header | ZeroAlloc.Rest | Supported | `ClientEmitter.cs:309-322`, `HttpError.cs:5-8`. Probe a: `status=422 retry-after=2` | none needed |
| 8 | Result error carries the response body, for the JSON detail on 422 | ZeroAlloc.Rest | **Missing** | `ClientEmitter.cs:320-321` builds `HttpError(StatusCode, headers)` without reading content. Probe a: `message=<null>` | none. Draft **R1**, filed as [ZeroAlloc.Rest#298](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/298) |
| 9 | Network failures, timeouts and malformed JSON become Result failures instead of exceptions | ZeroAlloc.Rest | **Missing** | `ClientEmitter.cs:288-295` catches, records and then `throw;`s. Probe a: `network: THREW HttpRequestException`, `timeout: THREW TaskCanceledException`, `bad-json: THREW JsonException` | none. Draft **R2**, filed as [ZeroAlloc.Rest#299](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/299) |
| 10 | Methods return `Result<T, JevError>` directly | ZeroAlloc.Rest | **Missing** | `ModelExtractor.cs:116-118` ignores `E`. `ClientEmitter.cs:313,320` hard-code `HttpError`. Probe b: CS0029 | none. Draft **R3**, filed as [ZeroAlloc.Rest#300](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/300) |
| 11 | A resilience proxy over a Result-returning method compiles | ZeroAlloc.Resilience | **Missing, a bug** | `ResilienceWriter.cs:87,105,211,249` emit `Result.Failure<T>(string)`, which does not exist in Results 1.2.3: `Result.cs:32`. Probe c: CS0308 | none. Draft **S1**, filed as [ZeroAlloc.Resilience#141](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/141) |
| 12 | Retry only on 429 and 529, driven by the returned Result | ZeroAlloc.Resilience | **Missing** | `ResilienceWriter.cs:168-175` retries only on a thrown exception. A returned failure is `return __result`. `NonThrowing` hard-codes `ResilienceError`: `ResilienceWriter.cs:204-208`. Probes c and d | none. Draft **S2**, filed as [ZeroAlloc.Resilience#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/142) |
| 13 | Honor `Retry-After` in the backoff | ZeroAlloc.Resilience | **Missing** | The backoff is the compile-time constant `BackoffMs * (1 << attempt)`: `ResilienceWriter.cs:184-187` | none. Draft **S3**, filed as [ZeroAlloc.Resilience#143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/143) |
| 14 | Resilience over an `internal` interface | ZeroAlloc.Resilience | **Missing** | `ResilienceWriter.cs:282` always emits `public static partial class ResilienceServiceCollectionExtensions`. Probe c: CS0703 | none. Draft **S5**, filed as [ZeroAlloc.Resilience#145](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/145) |
| 15 | Retry and timeout values configurable through DI, milestone 3 | ZeroAlloc.Resilience | **Missing, a bug** | `ResilienceWriter.cs:120,145,180,184-186` inline attribute constants. The injected `_retry` and `_timeout` fields are never read. Probe d: DI `MaxAttempts=6`, 3 attempts made | none. Draft **S4**, filed as [ZeroAlloc.Resilience#144](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/144) |
| 16 | `AddRestResilience` bridge usable with `Result<T, HttpError>` | ZeroAlloc.Rest.Resilience | **Missing**, blocked by 11, 12 and 14. The docs describe behaviour that does not exist | `docs/resilience.md:113-115` @ v1.3.5 says policies "inspect the `HttpError`". Contradicted by probes c and d | none. Draft **R5**, filed as [ZeroAlloc.Rest#302](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/302) |
| 17 | Spans with tags from the Result, on an internal interface | ZeroAlloc.Telemetry | Supported | `[Instrument]` with an internal proxy by default: `InstrumentAttribute.cs:41`. `[TraceTagFromResult(..., When = "IsSuccess")]`: `TraceTagFromResultAttribute.cs:30-76` | none needed |
| 18 | Metrics recording values from the response: tokens, confidence | ZeroAlloc.Telemetry | **Missing** | Only `[Count]`, which adds 1, and `[Histogram]`, which records elapsed ms: `ProxyWriter.cs:207,213,295` | none. Draft **T1**, filed as [ZeroAlloc.Telemetry#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Telemetry/issues/142) |
| 19 | `[Count]` skips failed Results | ZeroAlloc.Telemetry | **Missing** | `[Count]` fires on any non-throwing return: `CountAttribute.cs:4-6`, `ProxyWriter.cs:207` | none. Folded into **T1** |
| 20 | DI registration via ZeroAlloc.Inject without leaking public API | ZeroAlloc.Inject | **Partially** | `ZeroAllocInjectGenerator.cs:756-758` always emits a `public static class` in `Microsoft.Extensions.DependencyInjection`. Probe f: `public static IServiceCollection AddJevNetExtensionsDependencyInjectionServices` | none. Draft **I1**, filed as [ZeroAlloc.Inject#156](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/issues/156) |
| 21 | Typed client over `IHttpClientFactory` | ZeroAlloc.Rest | Supported | Generated `AddIJevApi` calls `AddHttpClient<TInterface, TClient>`: `DiEmitter.cs:42`. It is internal in 1.3.5 | none needed |
| 22 | `ValidateWithZeroAlloc()` for options | ZeroAlloc.Validation.Options | **Missing, a bug** | The nupkg holds only `lib/*/ZeroAlloc.Validation.Options.dll`, with no analyzer. There is no `ZeroAlloc.Validation.Options.Generator` package: NuGet returns 404. Probe e: CS1061 | none. #174 is the same defect in `.Inject`. Draft **V1**, filed as [ZeroAlloc.Validation#183](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/183) |
| 23 | Validators for Jev.Net's options and requests without public API leak | ZeroAlloc.Validation | **Missing** | `ValidatorGenerator.cs:345` always emits a `public sealed partial class`. Probe e: an internal `[Validate]` type gives CS9338 and CS0051 | none. Draft **V2**, filed as [ZeroAlloc.Validation#184](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/184) |
| 24 | `AllocationGate` in tests and the AOT smoke app | ZeroAlloc.TestHelpers | **Partially** | `AllocationGate.AssertBudgetValueTask` works, and the generated client completes synchronously over an in-memory handler. But the package also compiles `GeneratorSnapshot.cs` into the consumer, which needs Roslyn. Probe a: CS0234 and CS0246 | none. Draft **X1**, filed as [ZeroAlloc.TestHelpers#50](https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers/issues/50) |
| 25 | `Result<T, E>` type plus Map, Bind and Match | ZeroAlloc.Results | Supported | `Result2.cs:7-53`, `Extensions/ResultExtensions.cs`, `ResultAsyncExtensions.cs` | none needed |

**Totals:** 25 needs checked. 9 Supported, 2 Partially, 14 Missing. The gaps become 15 issue drafts: Rest 5, Resilience 5, Telemetry 1, Validation 2, Inject 1, TestHelpers 1. The Telemetry draft covers two needs.

### Probe outputs, verbatim

Probe a covers Rest 1.3.5 with an internal interface, internal DTOs, `Result<T, HttpError>`, `SystemTextJsonSerializer` over a source-gen context, and AOT. It gives the same output under `dotnet run` and as the native AOT exe:

```
client type public? False
200: success=True model=jev-1.13.0 tokens=7 seen=POST https://api.typesafe.ai/v1/systemone auth=Bearer k
422: failure=True status=422 message=<null> retry-after=2 headerKeys=[Retry-After]
529: failure=True status=529
network: THREW HttpRequestException
timeout: THREW TaskCanceledException
sync-completion with in-memory handler: True
AllocationGate.AssertBudgetValueTask: usable
bad-json: THREW JsonException
```

`dotnet publish -c Release -r win-x64` with `PublishAot=true` and TWAE produced no IL warnings. The first attempt failed only at the native link step because `vswhere.exe` was not on PATH. That is an environment issue: ILC had already finished, and the publish succeeded once PATH was fixed.

Probe b uses a custom error type:
```
IJevApi.g.cs(65,24): error CS0029: Cannot implicitly convert type 'Result<ModelList, ZeroAlloc.Rest.HttpError>' to 'Result<ModelList, Probe.JevError>'
```

Probe c adds `[Retry]` to a Rest interface that returns `Result<T, HttpError>`:
```
internal interface -> Probe_IJevApi.Resilience.g.cs(53,30): error CS0703: Inconsistent accessibility: constraint type 'IJevApi' is less accessible than 'ResilienceServiceCollectionExtensions.AddJevApiResilience<TImpl>(IServiceCollection)'
public interface   -> Probe_IJevApi.Resilience.g.cs(41,49): error CS0308: The non-generic method 'Result.Failure(string)' cannot be used with type arguments
NonThrowing = true -> Probe_IJevApi.Resilience.g.cs(41,16): error CS0029: Cannot implicitly convert type 'Result<ModelList, ResilienceError>' to 'Result<ModelList, HttpError>'
```

Probe d covers Resilience 1.3.4 runtime semantics:
```
Result failure from inner: attempts=1 (retried? False) failure=True
resolved RetryPolicy.MaxAttempts=6
422 exception: attempts=3 policy=Retry (non-transient 422 retried: True; DI MaxAttempts=6 honoured: False)
```

Probe e covers Validation 1.7.5:
```
public options + ValidateWithZeroAlloc(): error CS1061: 'OptionsBuilder<JevOptions>' does not contain a definition for 'ValidateWithZeroAlloc'
internal options: JevOptionsValidator.g.cs(11,29): error CS9338: Inconsistent accessibility: type 'JevOptions' is less accessible than class 'JevOptionsValidator'
```

Probe f covers Inject 1.7.6 with an internal `[Singleton]` type in an assembly named `Jev.Net.Extensions.DependencyInjection`:
```
namespace Microsoft.Extensions.DependencyInjection
    public static class JevNetExtensionsDependencyInjectionServicesServiceCollectionExtensions
        public static IServiceCollection AddJevNetExtensionsDependencyInjectionServices(this IServiceCollection services)
```

Probe g covers Rest 1.3.5 with an interface-level `[Serializer]` and two clients in one container:
```
JevApiClient ctor: HttpClient, IRestSerializer
IRestSerializer registrations: SystemTextJsonSerializer
```
The interface-level serializer was not injected. When the host registered its Rest client first, the library's `UseSerializer<JevSerializer>()` was silently dropped.

---

## 2. Issue drafts, filed 2026-09-24

No draft duplicates an open or closed issue. Each body is self-contained.

### R1: ZeroAlloc.Rest. `HttpError` does not expose the response body

**Filed:** [ZeroAlloc.Rest#298](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/298)

**Title:** `Result<T, HttpError>`: expose the error response body on `HttpError`

**Body:**

**Summary**
On the non-success branch of a `Result<T, HttpError>` method, the generated client builds `HttpError` from the status code and response headers only. The response body is never read, so an API's JSON error detail cannot be recovered.

**Motivation**
Jev.Net is a client SDK for TypeSafe AI's Jev API, and it returns `Result<T, JevError>` from every call. The API documents that a `422 Unprocessable Entity` response body "details the offending field". Without that body, Jev.Net can only report "422" to the user and not which field failed validation. The same applies to 401, 429 and 529, which also carry a JSON body per the API docs.

**Current behaviour, ZeroAlloc.Rest 1.3.5**
- `src/ZeroAlloc.Rest/HttpError.cs:5-8`: the record has `StatusCode`, `Headers` and an optional `Message`.
- `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs:316-321`: the failure branch copies `response.Headers` and calls `new HttpError` with the status code and headers. `Message` is never set and `response.Content` is never read.
- Probe: a stub handler returned 422 with the body `{"detail":"questions missing"}`. Output: `422: failure=True status=422 message=<null>`.
- Content headers such as `Content-Type` are also missing, because only `response.Headers` is copied.

**Proposed API and behaviour**
Add the raw body and the content headers to `HttpError`, read only on the failure branch:

```csharp
public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null)
{
    public ReadOnlyMemory<byte> Body { get; init; }
    public string? ContentType { get; init; }
}
```

In the generated failure branch:

```csharp
var __body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
var __error = new HttpError(response.StatusCode, headers)
{
    Body = __body,
    ContentType = response.Content.Headers.ContentType?.MediaType,
};
return Result<T, HttpError>.Failure(__error);
```

A byte buffer keeps the choice of decoding with the caller. For example, Jev.Net would deserialize it with its own source-generated `JsonTypeInfo`. An optional size cap, such as `ZeroAllocClientOptions.MaxErrorBodyBytes` with a default of 64 KiB, bounds the read.

**Alternatives considered**
- Set `Message` to the body text. That forces UTF-8 decoding and mixes a human message with a payload.
- Have the caller do its own `HttpClient` call on failure. That defeats the generated client.
- Use a `DelegatingHandler` that buffers the body into `HttpRequestMessage.Options`. It works, but it is invisible, it allocates on success too, and `HttpError` has no link back to the request.

**Compatibility**
The change is additive: new init-only properties with defaults. The success path is unchanged. The failure path gains one body read, which is the expected cost of an error.

---

### R2: ZeroAlloc.Rest. Transport, timeout and deserialization failures throw from Result-returning methods

**Filed:** [ZeroAlloc.Rest#299](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/299)

**Title:** `Result<T, HttpError>` methods still throw on network failure, timeout and malformed JSON

**Body:**

**Summary**
A method declared as returning `ValueTask<Result<T, HttpError>>` still throws `HttpRequestException`, `TaskCanceledException` and `JsonException`. The Result only covers non-2xx status codes, so callers who chose a Result signature to avoid exceptions must still wrap every call in try/catch.

**Motivation**
Jev.Net's contract is that every call returns `Result<T, JevError>` and never throws for API or transport errors. Its error model lists network failures and timeouts next to 401, 422, 429 and 529. Today Jev.Net would need a try/catch around every generated call, which duplicates what the generated client already does in its own catch block.

**Current behaviour, ZeroAlloc.Rest 1.3.5**
- `src/ZeroAlloc.Rest.Generator/ClientEmitter.cs:288-295`: `catch (global::System.Exception __ex)` records the span status and duration, then `throw;`. This applies to Result-returning methods too.
- `ClientEmitter.cs:311-313`: deserialization on the success branch is not guarded. A malformed 200 body throws `JsonException`.
- Probe with a stub handler:
  ```
  network: THREW HttpRequestException
  timeout: THREW TaskCanceledException
  bad-json: THREW JsonException
  ```

**Proposed API and behaviour**
For methods whose return type is a Result, map the failures below to a failure value. Keep throwing for caller-requested cancellation.

```csharp
public enum HttpErrorKind { Status, Transport, Timeout, Deserialization }

public sealed record HttpError(
    HttpStatusCode StatusCode,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Headers,
    string? Message = null)
{
    public HttpErrorKind Kind { get; init; } = HttpErrorKind.Status;
    public Exception? Exception { get; init; }
}
```

Generated catch blocks, emitted only when `ReturnsResult` is true:

```csharp
catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
catch (OperationCanceledException __ex)
{
    var __error = new HttpError(0, s_noHeaders, __ex.Message) { Kind = HttpErrorKind.Timeout, Exception = __ex };
    return Result<T, HttpError>.Failure(__error);
}
catch (HttpRequestException __ex)
{
    var __error = new HttpError(0, s_noHeaders, __ex.Message) { Kind = HttpErrorKind.Transport, Exception = __ex };
    return Result<T, HttpError>.Failure(__error);
}
```

Treat a `JsonException` from the deserializer the same way, with `Kind = Deserialization` and the real status code. If a flag is preferred over changing the default, use `[ZeroAllocRestClient(ResultCapturesExceptions = true)]`.

**Alternatives considered**
- A caller-side try/catch around every call. That is what Jev.Net must do today, and it is repetitive and easy to miss on new methods.
- A `DelegatingHandler` that converts exceptions into synthetic responses. That hides real failures behind fake status codes and cannot catch deserialization errors.

**Compatibility**
This is a behaviour change for Result-returning methods only. Call sites that relied on the exception would now receive a failure value. Shipping it behind an opt-in flag first, or in a minor release with a changelog note, avoids surprises. Methods that do not return a Result are unaffected.

---

### R3: ZeroAlloc.Rest. Custom error types in `Result<T, TError>`

**Filed:** [ZeroAlloc.Rest#300](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/300)

**Title:** Support `Result<T, TError>` with a user-defined error type via an error mapper

**Body:**

**Summary**
The generator recognises `Result<T, E>` but ignores `E` and always emits `Result<T, HttpError>`. Declaring any other error type fails to compile with CS0029.

**Motivation**
Jev.Net exposes `Result<T, JevError>`, where `JevError` is a closed domain type covering Unauthorized, Validation with field detail, RateLimited with a retry delay, Overloaded, Network and Timeout. With only `HttpError` available, Jev.Net has to declare a second internal interface returning `HttpError`, write a hand-written wrapper per method that maps the error, and keep the two interfaces in sync. A mapper hook lets the generated client return the domain type directly.

**Current behaviour, ZeroAlloc.Rest 1.3.5**
- `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs:116-118`: `returnsResult` matches `ZeroAlloc.Results.Result<T, E>` and takes only `TypeArguments[0]`.
- `ClientEmitter.cs:313` and `:320` emit `Result<T, ZeroAlloc.Rest.HttpError>` literally.
- Probe: `ValueTask<Result<ModelList, JevError>> ModelsAsync` gives `error CS0029: Cannot implicitly convert type 'Result<ModelList, ZeroAlloc.Rest.HttpError>' to 'Result<ModelList, Probe.JevError>'`.

**Proposed API and behaviour**
Add an error-mapper contract, resolved from DI like the serializer:

```csharp
public interface IHttpErrorMapper<TError>
{
    ValueTask<TError> MapAsync(HttpResponseMessage response, CancellationToken ct);
    TError MapException(Exception exception);
}
```

When `E` is not `HttpError`, the generator adds an `IHttpErrorMapper<E>` constructor parameter. The failure branch calls `await _errorMapper.MapAsync(response, ct)`, and, together with R2, the transport catch blocks call `_errorMapper.MapException(ex)`. The generated `Add{I}` method does a `TryAddSingleton` of the mapper type given in options:

```csharp
Action<ZeroAllocClientOptions> configure = o =>
{
    o.BaseAddress = new Uri("https://api.typesafe.ai/");
    o.UseErrorMapper<JevErrorMapper>();
};
services.AddIJevApi(configure);
```

If `E` is not `HttpError` and no mapper is resolvable, report a generator diagnostic instead of emitting code that fails later.

**Alternatives considered**
- Keep `HttpError` and map in a hand-written wrapper. This works with R1 and R2 in place, but it adds a class and one extra async state machine per call. The allocation budget matters here.
- An implicit conversion from `HttpError` to `JevError`. The generator would still need to use it, and a conversion cannot be async to read the body.

**Compatibility**
Additive. Methods returning `Result<T, HttpError>` keep today's code path exactly.

---

### R4: ZeroAlloc.Rest. Per-client serializer: the interface-level `[Serializer]` is ignored

**Filed:** [ZeroAlloc.Rest#301](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/301)

**Title:** Interface-level `[Serializer]` is silently ignored, so library clients cannot isolate their serializer from the host

**Body:**

**Summary**
`SerializerAttribute` is declared as valid on interfaces, and the generator reads it, but neither emitter uses it. Every generated client injects the single container-wide `IRestSerializer`, and the generated `Add{I}` registers it with `TryAddSingleton`. A library that ships a ZeroAlloc.Rest client therefore shares, and can silently lose, its serializer to whatever the host application registered first.

**Motivation**
Jev.Net is a NuGet library. Its client needs a `SystemTextJsonSerializer` built over its own source-generated `JsonSerializerContext`, with snake_case naming and polymorphic answers. A host application that also uses ZeroAlloc.Rest registers its own `IRestSerializer`, typically with `JsonSerializerDefaults.Web`. Whichever registration runs first wins for both clients, so the Jev.Net client can end up deserializing with the wrong naming policy, or with reflection metadata under Native AOT.

**Current behaviour, ZeroAlloc.Rest 1.3.5**
- `src/ZeroAlloc.Rest/Attributes/ParameterAttributes.cs:34-38`: `[AttributeUsage(AttributeTargets.Interface | AttributeTargets.Method)]`.
- `src/ZeroAlloc.Rest.Generator/ModelExtractor.cs:39`: the interface-level type is read into `ClientModel.SerializerTypeName`. A repo-wide grep shows that property is never read by `ClientEmitter` or `DiEmitter`. Only method-level overrides are used, at `ClientModel.cs:20-29`.
- `ClientEmitter.cs:71-75`: the constructor always takes `ZeroAlloc.Rest.IRestSerializer serializer`.
- `DiEmitter.cs:38-39`: `services.TryAddSingleton` registers `options.SerializerType` under the service type `IRestSerializer`.
- Probe: an interface carrying `[Serializer]` for a custom JevSerializer, with the host's client registered first:
  ```
  JevApiClient ctor: HttpClient, IRestSerializer
  IRestSerializer registrations: SystemTextJsonSerializer
  ```
  The interface-level serializer is not injected, and the library's `UseSerializer<JevSerializer>()` is dropped without a diagnostic.

**Proposed API and behaviour**
1. Honour the interface-level `[Serializer]`. When present, the generated constructor takes that concrete type instead of `IRestSerializer`, and the generated `Add{I}` does a `TryAddSingleton` of that concrete type, as method-level overrides already do.
2. Make `UseSerializer<T>()` per client. Register it as a keyed service under the interface name and resolve it by key in the typed-client factory, so it never collides with another client's registration.
3. Accept an instance for serializers that need constructor arguments:
   ```csharp
   var serializer = new SystemTextJsonSerializer(JevJson.Options);
   Action<ZeroAllocClientOptions> configure = o =>
   {
       o.BaseAddress = new Uri("https://api.typesafe.ai/");
       o.UseSerializer(serializer);
   };
   services.AddIJevApi(configure);
   ```

**Alternatives considered**
- Method-level `[Serializer]` on every method. It works today, but the client constructor still requires a global `IRestSerializer`, so a dummy registration is needed, and every new method must remember the attribute.
- Construct the client by hand instead of using the generated `Add{I}`. That works, but it gives up the generated DI and duplicates `AddHttpClient` wiring.

**Compatibility**
Point 1 changes the generated constructor for interfaces that already carry `[Serializer]`. Those interfaces currently compile but ignore it, so honouring it fixes silent misbehaviour. Points 2 and 3 are additive.

---

### R5: ZeroAlloc.Rest. `docs/resilience.md` describes Result-aware retry that does not exist

**Filed:** [ZeroAlloc.Rest#302](https://github.com/ZeroAlloc-Net/ZeroAlloc.Rest/issues/302)

**Title:** docs: "Result Returns and Error Handling" in resilience.md does not match Resilience 1.3.4 behaviour

**Body:**

**Summary**
`docs/resilience.md:113-115` states that when a method returns `Result<T, HttpError>`, "resilience policies inspect the `HttpError` rather than catching exceptions", configured with `NonThrowing = true`. With ZeroAlloc.Rest 1.3.5 and ZeroAlloc.Resilience 1.3.4 that combination does not compile, and even where it compiles, policies never inspect a returned error.

**Motivation**
Jev.Net planned its retry phase around this paragraph. It needs retry on 429 and 529 for a client whose methods return `Result<T, HttpError>`. The paragraph led to a design that cannot build.

**Current behaviour, verified**
- `[Retry]` on a `[ZeroAllocRestClient]` interface returning `Result<T, HttpError>` gives `error CS0308: The non-generic method 'Result.Failure(string)' cannot be used with type arguments` in the generated proxy.
- With `NonThrowing = true`: `error CS0029: Cannot implicitly convert type 'Result<ModelList, ResilienceError>' to 'Result<ModelList, HttpError>'`.
- In the Resilience generator, `ResilienceWriter.cs:168-175` returns whatever the inner call returned and retries only on thrown exceptions.
- The Quick Start at `docs/resilience.md:26` imports `ZeroAlloc.Resilience.Attributes`, but the attributes live in the `ZeroAlloc.Resilience` namespace. It also says the generator must be added as a separate analyzer package, but it is bundled in `ZeroAlloc.Resilience`.

**Proposed change**
Until Result-aware retry ships in ZeroAlloc.Resilience, replace the section with an accurate statement: policies act only on exceptions, so use methods that throw on non-success, or handle Result failures in the caller. Fix the namespace and the install instructions. Link the tracking issues in ZeroAlloc.Resilience for Result support and status-code predicates.

**Alternatives considered**
Leave the docs until the feature exists. That keeps sending users to a design that cannot build.

**Compatibility**
Docs only.

---

### S1: ZeroAlloc.Resilience. Result-returning methods generate code that does not compile

**Filed:** [ZeroAlloc.Resilience#141](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/141)

**Title:** Generated proxy calls `Result.Failure<T>(string)`, which does not exist in ZeroAlloc.Results 1.2.x, so every `Result` return type fails with CS0308

**Body:**

**Summary**
For any policy-wrapped method whose return type is a `ZeroAlloc.Results.Result*` and that is not `NonThrowing`, the generator emits `global::ZeroAlloc.Results.Result.Failure<TInner>("...")`. ZeroAlloc.Results 1.2.3 only has the non-generic `Result.Failure(string)`. The generated proxy fails to compile, which blocks the documented "Result return types" feature entirely.

**Motivation**
Jev.Net wraps a ZeroAlloc.Rest client whose methods return `ValueTask<Result<T, HttpError>>`. Adding `[Retry]` to that interface breaks the build.

**Current behaviour, ZeroAlloc.Resilience 1.3.4 with ZeroAlloc.Results 1.2.3**
- `src/ZeroAlloc.Resilience.Generator/ResilienceWriter.cs:87`, `:105`, `:211` and `:249` emit `Result.Failure<{method.InnerReturnType}>(...)`.
- `ResilienceGenerator.cs:312-320`: `UnwrapReturnType` sets `isResult` for any type whose name starts with `ZeroAlloc.Results.Result`, so `Result<T>` and `Result<T, E>` both take this path. `InnerReturnType` is then the whole Result type. For `Result<T, E>`, even a generic factory would produce a Result nested in a Result.
- ZeroAlloc.Results `src/ZeroAlloc.Results/Result.cs:32`: `public static Result Failure(string error)` is the only factory on the non-generic `Result`.
- Probe: `[Retry(MaxAttempts = 3, BackoffMs = 10)]` on a public interface with `ValueTask<Result<ModelList, HttpError>> ModelsAsync(CancellationToken ct)` gives `Probe_IJevApi.Resilience.g.cs(41,49): error CS0308: The non-generic method 'Result.Failure(string)' cannot be used with type arguments`.
- `docs/guides/result-return-types.md` documents this exact generated code as working. The repo tests only cover the `NonThrowing` path with `Result<T, ResilienceError>`.

**Proposed behaviour**
Emit a failure that matches the declared return type:
- For `Result<T>`, emit `Result<T>.Failure(message)`.
- For `Result<T, E>` where `E` is `ResilienceError`, emit `Result<T, ResilienceError>.Failure` with a new `ResilienceError`.
- For `Result<T, E>` with any other `E`, return the last inner Result when one exists. When no inner Result exists, for example on an open circuit or a rate-limit rejection, report a diagnostic unless the user supplies a factory. See the companion issue on retrying Result failures for the factory shape.

Add generator tests that compile the output for `Result<T>`, for `Result<T, ResilienceError>` and for `Result<T, E>` with a foreign `E`.

**Alternatives considered**
Add `public static Result<T> Failure<T>(string error)` to ZeroAlloc.Results. That fixes `Result<T>` only. `Result<T, E>` would still be wrapped as a Result inside a Result.

**Compatibility**
A bug fix. The code is currently uncompilable, so no working consumer depends on it.

---

### S2: ZeroAlloc.Resilience. Retry on returned Result failures, filtered by a predicate

**Filed:** [ZeroAlloc.Resilience#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/142)

**Title:** Retry when the inner call returns a failed `Result`, with a predicate choosing which failures are transient

**Body:**

**Summary**
The retry loop only reacts to thrown exceptions. A method returning `Result<T, E>` whose inner call returns a failure value, such as an HTTP 429, is returned immediately and never retried. There is also no way to say which failures are transient: for the throwing path, every exception is retried, including non-transient ones such as HTTP 422.

**Motivation**
Jev.Net must retry TypeSafe API calls on `429 Too Many Requests` and on the non-standard `529 Overloaded`, and only on those two. Its transport is a ZeroAlloc.Rest client returning `Result<T, HttpError>`, so these statuses arrive as failure values, not exceptions. Retrying 401 or 422 would be wrong, because those requests cannot succeed on retry.

**Current behaviour, ZeroAlloc.Resilience 1.3.4**
- `src/ZeroAlloc.Resilience.Generator/ResilienceWriter.cs:165-175`: `var __result = await _inner.M(...)`, then `return __result;`. Only the `catch (global::System.Exception __ex)` branch loops.
- `ResilienceWriter.cs:204-208`: `NonThrowing` always returns `Result<T, ResilienceError>`, so it cannot be combined with an inner `Result<T, HttpError>`. Probe: `error CS0029: Cannot implicitly convert type 'Result<ModelList, ResilienceError>' to 'Result<ModelList, HttpError>'`.
- Runtime probe with an inner call that returns a failed `Result<string, ResilienceError>`: `attempts=1 (retried? False)`.
- Runtime probe with an inner call that throws `HttpRequestException` with status 422: `attempts=3 ... non-transient 422 retried: True`.

**Proposed API and behaviour**
A static predicate method named on the attribute, resolved at compile time. The name is checked by the generator, and there is no delegate allocation:

```csharp
[ZeroAllocRestClient]
[Retry(MaxAttempts = 4, BackoffMs = 500, Jitter = true, RetryWhen = "IsTransient")]
internal interface IJevApi
{
    [Post("v1/systemone")]
    ValueTask<Result<SystemOneResponse, HttpError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct);

    static bool IsTransient(HttpError e) => (int)e.StatusCode is 429 or 529;
}
```

Generated loop body when the return type is `Result<T, E>` and `RetryWhen` is set:

```csharp
var __result = await _inner.AskAsync(body, __ct).ConfigureAwait(false);
if (__result.IsSuccess) return __result;
var __transient = IJevApi.IsTransient(__result.Error);
if (!__transient) return __result;
__last = __result;
```

On exhaustion, return `__last`, the last failed Result, unchanged, so the caller sees the real final error. An optional `RetryOnException = "MethodName"`, taking a `bool M(Exception)`, gives the throwing path the same filtering.

**Alternatives considered**
- Make the inner call throw on 429 and 529 and catch the exception. That means allocating an exception per retry and losing the typed error, and it still retries 422 unless filtered.
- A hand-written retry loop in the consumer. This is what Jev.Net will do today, and it duplicates the generator's job.

**Compatibility**
Additive. Without `RetryWhen`, today's exception-only semantics are unchanged.

---

### S3: ZeroAlloc.Resilience. Honour a server-supplied retry delay such as `Retry-After`

**Filed:** [ZeroAlloc.Resilience#143](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/143)

**Title:** Allow the backoff delay to come from the failed result, for example an HTTP `Retry-After` header

**Body:**

**Summary**
Backoff is always `BackoffMs * 2^attempt`, plus optional jitter, computed from compile-time constants. When the server says how long to wait, as HTTP 429 and 503-style responses do with `Retry-After`, the proxy cannot use it.

**Motivation**
Jev.Net's roadmap requires 429 and 529 retries that honour `Retry-After` when present, falling back to exponential backoff when it is absent. Retrying earlier than the server asked wastes a request against a rate limit. Waiting longer adds needless latency.

**Current behaviour, ZeroAlloc.Resilience 1.3.4**
- `src/ZeroAlloc.Resilience.Generator/ResilienceWriter.cs:184-187`: the delay passed to `Task.Delay` is `BackoffMs * 2^attempt`, with both values inlined as literals.
- `src/ZeroAlloc.Resilience/RetryPolicy.cs:37-43`: `GetBackoffMs(int attempt)` has no input from the failure.
- There is no attribute property or hook for a per-failure delay.

**Proposed API and behaviour**
A static delay-hint method named on the attribute, called with the failed value or exception. It returns `null` to fall back to the computed backoff:

```csharp
[Retry(MaxAttempts = 4, BackoffMs = 500, RetryWhen = "IsTransient", DelayHint = "RetryAfter", MaxDelayMs = 30_000)]
internal interface IJevApi
{
    static TimeSpan? RetryAfter(HttpError e)
    {
        var found = e.Headers.TryGetValue("Retry-After", out var values);
        if (!found || values.Count == 0) return null;
        return int.TryParse(values[0], out var seconds) ? TimeSpan.FromSeconds(seconds) : null;
    }
}
```

Generated code:

```csharp
var __hint = IJevApi.RetryAfter(__result.Error);
var __delay = computedBackoff;
if (__hint is { } h)
{
    var __hintMs = h.TotalMilliseconds > MaxDelayMs ? MaxDelayMs : h.TotalMilliseconds;
    __delay = Convert.ToInt32(__hintMs);
}
await Task.Delay(__delay, __ct).ConfigureAwait(false);
```

`MaxDelayMs` caps a hostile or buggy header. The delay must still observe the total timeout token. ZeroAlloc.Rest could ship a ready-made `HttpErrorRetry.RetryAfter` helper that parses both delta-seconds and HTTP-date forms, so users do not each reimplement it.

**Alternatives considered**
- A `DelegatingHandler` that sleeps on 429. That hides the wait from the resilience timeout and circuit breaker, and it blocks the handler pipeline.
- Ignore `Retry-After`. That violates the server's guidance and risks longer rate limiting.

**Compatibility**
Additive. Without `DelayHint`, the backoff is unchanged. This depends on the Result-aware retry issue for the `Result<T, E>` case. For the exception path, the hint method can take the `Exception`.

---

### S4: ZeroAlloc.Resilience. Policy objects from DI are ignored because the generated proxy inlines attribute constants

**Filed:** [ZeroAlloc.Resilience#144](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/144)

**Title:** Generated proxy ignores the injected `RetryPolicy` and `TimeoutPolicy` and uses attribute literals, so policies cannot be configured at runtime

**Body:**

**Summary**
The proxy constructor takes `RetryPolicy` and `TimeoutPolicy` instances, and the generated DI extension registers them as singletons. But the generated method bodies never read those fields. `MaxAttempts`, `BackoffMs`, `Jitter`, `PerAttemptTimeoutMs` and the total timeout are all emitted as integer literals from the attribute. Registering a different policy, for example from configuration, has no effect.

**Motivation**
Jev.Net milestone 3 exposes retry and timeout settings through DI and `IOptions`, so an application can tune attempts and backoff for its rate limits without recompiling. The `ZeroAlloc.Rest.Resilience` docs also show resolving `RetryPolicy` from the container and passing it to the proxy, which suggests it is honoured.

**Current behaviour, ZeroAlloc.Resilience 1.3.4**
- `src/ZeroAlloc.Resilience.Generator/ResilienceWriter.cs:145`: `for (int __attempt = 0; __attempt < {retry.MaxAttempts}; ...)`, a literal.
- `:180`: `if (__attempt == {retry.MaxAttempts - 1}) break;`, a literal.
- `:184-187`: the backoff expression is built from `retry.BackoffMs` and `retry.Jitter` literals.
- `:120` and `:153`: `CancelAfter({method.Timeout!.TotalMs})` and `CancelAfter({retry.PerAttemptTimeoutMs})`, literals.
- `:43-44` and `:58-59`: the `_retry` and `_timeout` fields are assigned but never read.
- Runtime probe: `AddThrowSvcResilience<Throws>()`, then `services.AddSingleton` of `new RetryPolicy` with `maxAttempts: 6`. Output: `resolved RetryPolicy.MaxAttempts=6`, then `attempts=3 ... DI MaxAttempts=6 honoured: False`.

**Proposed behaviour**
Read the values from the injected policy objects in the generated body. Keep the attribute values as the defaults the DI extension registers:

```csharp
for (int __attempt = 0; __attempt < _retry.MaxAttempts; __attempt++)
{
    // ...
    if (__attempt == _retry.MaxAttempts - 1) break;
    var __delay = _retry.GetBackoffMs(__attempt);
    await Task.Delay(__delay, __ct).ConfigureAwait(false);
}
```

The same applies to `_timeout.TotalMs` and `_retry.PerAttemptTimeoutMs`. Reading two ints from a sealed singleton costs effectively nothing and does not allocate. Optionally add an overload of the generated extension that takes `Action<RetryPolicyOptions>`, or binds from `IOptions`, so the values can come from configuration.

Method-level overrides need separate policy instances per method, for example keyed by method name, or the generator keeps literals only for methods whose attributes differ from the interface-level ones. The issue should settle which.

**Alternatives considered**
Document that policies are compile-time only and remove the constructor parameters. That is consistent, but it rules out configuration-driven tuning, which is a common requirement for rate-limited APIs.

**Compatibility**
Behaviour changes only for users who register a policy different from the attribute values, and those users are currently getting silently wrong behaviour. Without an override, the effective values are identical.

---

### S5: ZeroAlloc.Resilience. Generated DI extension is always `public`, which breaks internal interfaces

**Filed:** [ZeroAlloc.Resilience#145](https://github.com/ZeroAlloc-Net/ZeroAlloc.Resilience/issues/145)

**Title:** Generated `Add{Service}Resilience<TImpl>` is always public: CS0703 on internal interfaces and public-API leak in libraries

**Body:**

**Summary**
The generated extension class `ResilienceServiceCollectionExtensions` is always `public`, and its method constrains `TImpl` to the service interface. When the interface is `internal`, this is error CS0703. The proxy class itself is already emitted `internal`. ZeroAlloc.Rest fixed the same problem for its generated client and DI extension in 1.3.5, via issue #295 and PR #296.

**Motivation**
Jev.Net keeps its transport interface `internal`. Its public API is a hand-written `JevClient`, tracked by PublicApiAnalyzers. Adding `[Retry]` to that interface breaks the build. Even with a public interface, the generated extension would add an unwanted public type to the library's API surface.

**Current behaviour, ZeroAlloc.Resilience 1.3.4**
- `src/ZeroAlloc.Resilience.Generator/ResilienceWriter.cs:40`: the proxy is `internal sealed class`, which is correct.
- `ResilienceWriter.cs:282-290`: `public static partial class ResilienceServiceCollectionExtensions` with `where TImpl : class, {InterfaceFqn}`.
- Probe with an internal interface: `Probe_IJevApi.Resilience.g.cs(53,30): error CS0703: Inconsistent accessibility: constraint type 'IJevApi' is less accessible than 'ResilienceServiceCollectionExtensions.AddJevApiResilience<TImpl>(IServiceCollection)'`.

**Proposed behaviour**
Follow the interface's effective accessibility, as ZeroAlloc.Rest 1.3.5 does in `ModelExtractor.IsEffectivelyPublic` and `ClientModel.Accessibility`. Emit `internal` for non-public interfaces, into a separate partial class name such as `InternalResilienceServiceCollectionExtensions`, because partial declarations must agree on accessibility.

**Alternatives considered**
Make the interface public. That compiles, but it leaks the transport interface, the extension, and every DTO the interface references into the library's public API.

**Compatibility**
Public interfaces are unchanged. Internal interfaces go from a compile error to working.

---

### T1: ZeroAlloc.Telemetry. Record metric values from the return value, and skip failed Results

**Filed:** [ZeroAlloc.Telemetry#142](https://github.com/ZeroAlloc-Net/ZeroAlloc.Telemetry/issues/142)

**Title:** Metrics from the result: `[CountFromResult]` and `[HistogramFromResult]`, plus a `When` guard on `[Count]`

**Body:**

**Summary**
`[Count]` can only add 1, and `[Histogram]` can only record elapsed milliseconds. There is no way to record a value carried by the return value, such as tokens consumed or a model's confidence. `[Count]` also fires on any non-throwing return, so a method returning a failed `Result<T, E>` is counted as a success.

**Motivation**
Jev.Net wraps TypeSafe's Jev API. Each response carries `usage.input_tokens` and `usage.output_tokens`, and input tokens are what is billed. Answers carry a `confidence` in [0, 1]. Jev.Net's milestone 3 requires metrics for token usage and confidence next to latency. These values are only known after the call returns. `[TraceTagFromResult]` can already put them on the span, with a `When` guard for Results, but spans are sampled and not aggregatable the way a counter is.

**Current behaviour, ZeroAlloc.Telemetry 1.6.3**
- `src/ZeroAlloc.Telemetry/CountAttribute.cs:4-6`: "Increments a Counter of long by 1 after a successful, non-throwing call".
- `src/ZeroAlloc.Telemetry/HistogramAttribute.cs:4-7`: records elapsed ms.
- `src/ZeroAlloc.Telemetry.Generator/ProxyWriter.cs:207`: `_{field}.Add(1);`. `:213` and `:295`: `.Record` with the elapsed `Stopwatch` time in milliseconds.
- `[TraceTagFromResult(Name, Member) { When = "IsSuccess" }]` already implements member-path reading with a boolean guard, in `TraceTagFromResultAttribute.cs:30-76`. The same machinery could feed metrics.

**Proposed API and behaviour**

```csharp
[Instrument("Jev.Net")]
internal interface IJevTransport
{
    [Trace("jev.systemone")]
    [Count("jev.requests", When = "IsSuccess")]
    [CountFromResult("jev.tokens.input", "Value.Usage.InputTokens", When = "IsSuccess")]
    [CountFromResult("jev.tokens.output", "Value.Usage.OutputTokens", When = "IsSuccess")]
    [HistogramFromResult("jev.answer.confidence", "Value.MinConfidence", When = "IsSuccess", Unit = "1")]
    ValueTask<Result<SystemOneResponse, JevError>> AskAsync(SystemOneRequest request, CancellationToken ct);
}
```

- `CountFromResult` creates a `Counter<long>` and calls `Add` with the member value. The member must be convertible to long; the generator reports a diagnostic otherwise.
- `HistogramFromResult` creates a `Histogram<double>` and calls `Record` with the member value.
- `When` has the same semantics as on `[TraceTagFromResult]`. The member is not read unless the guard is true.
- `When` on `[Count]` and `[Histogram]` applies the same guard to the existing instruments.
- Optional: `Unit` and `Description` pass through to `CreateCounter` and `CreateHistogram`, and `[MetricTagFromResult]` adds a dimension such as the versioned model id from `Value.Model`. It could be a separate follow-up.

`AllowMultiple = true` on both new attributes.

**Alternatives considered**
- Hand-written `Meter` code in a wrapper. That is what Jev.Net will do without this. It works, but it bypasses the generator for exactly the metrics that matter most.
- A span tag only. Tags are not aggregatable as metrics, and are lost when the span is not sampled.

**Compatibility**
Additive. `When` defaults to `null`, which keeps today's unconditional behaviour for `[Count]` and `[Histogram]`.

---

### V1: ZeroAlloc.Validation. `ZeroAlloc.Validation.Options` ships no generator, so `ValidateWithZeroAlloc()` never exists

**Filed:** [ZeroAlloc.Validation#183](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/183)

**Title:** ZeroAlloc.Validation.Options 1.7.5 package contains no analyzer: `ValidateWithZeroAlloc()` is never generated

**Body:**

**Summary**
`docs/options.md` says `ValidateWithZeroAlloc()` is source-generated per `[Validate]` class once `ZeroAlloc.Validation.Options` is installed. The published package contains only the runtime assembly. The `OptionsValidationEmitter` generator is not packed, and there is no separate generator package, so the extension method is never emitted.

**Motivation**
Jev.Net milestone 3 binds `JevClientOptions`, holding the API key, base address, model and retry settings, from configuration, and must fail fast on invalid values with `ValidateOnStart()`. It planned to use `ValidateWithZeroAlloc()` for this.

**Current behaviour, 1.7.5**
- `zeroalloc.validation.options.1.7.5.nupkg` contains only `lib/net8.0`, `lib/net9.0` and `lib/net10.0` builds of `ZeroAlloc.Validation.Options.dll`. There is no `analyzers/dotnet/cs/` and no `build/*.targets`.
- `src/ZeroAlloc.Validation.Options/ZeroAlloc.Validation.Options.csproj:18-24` references the generator projects with `OutputItemType="Analyzer"`, which only applies inside the repo and is not packed.
- `https://api.nuget.org/v3-flatcontainer/zeroalloc.validation.options.generator/index.json` returns 404.
- Probe with `ZeroAlloc.Validation`, `ZeroAlloc.Validation.Generator` and `ZeroAlloc.Validation.Options` 1.7.5 and a public `[Validate]` options class: the per-type `JevOptionsValidator.g.cs` is generated, but `services.AddOptions<JevOptions>().ValidateWithZeroAlloc()` gives `error CS1061: 'OptionsBuilder<JevOptions>' does not contain a definition for 'ValidateWithZeroAlloc'`.
- A second defect in the same generator: `src/ZeroAlloc.Validation.Options.Generator/OptionsValidationEmitter.cs:19` filters on `node is ClassDeclarationSyntax`, so `[Validate]` records are skipped. This is the same defect #174 fixed for `ZeroAlloc.Validation.Inject`.

**Proposed fix**
Pack `ZeroAlloc.Validation.Options.Generator.dll`, and the `ZeroAlloc.Validation.Inject` assembly it loads for `ValidatorRegistrationEmitter`, under `analyzers/dotnet/cs/` in the Options package. Change the predicate to `node is TypeDeclarationSyntax`, or add `RecordDeclarationSyntax`. Add a packaging test that restores the built nupkg into a sample project and calls `ValidateWithZeroAlloc()`, as the fix for #174 did for Inject.

**Alternatives considered**
Register `IValidateOptions<T>` by hand with `ZeroAllocOptionsValidator<T>` and the generated validator. That works, but it is the boilerplate the package exists to remove.

**Compatibility**
Packaging fix. Consumers who registered the validator by hand as a workaround keep working, because the generated code uses `TryAddSingleton`.

---

### V2: ZeroAlloc.Validation. Generated validators and options extensions ignore the model's accessibility

**Filed:** [ZeroAlloc.Validation#184](https://github.com/ZeroAlloc-Net/ZeroAlloc.Validation/issues/184)

**Title:** Generated validator is always `public`: CS9338 for internal `[Validate]` types, and public-API leak in libraries

**Body:**

**Summary**
The validator generator always emits `public sealed partial class {Model}Validator : ValidatorFor<{Model}>`. For an `internal` model this is a compile error. For a public model in a library, it adds a public validator type to the library's API surface. The Options generator likewise always emits `public static class ZeroAllocOptionsValidationExtensions`, in the global namespace.

**Motivation**
Jev.Net is a NuGet library whose public surface is tracked by PublicApiAnalyzers. Its options type and request types are public, but their validators are implementation details. Internal helper types that need validation cannot use `[Validate]` at all today. A global-namespace public class named `ZeroAllocOptionsValidationExtensions` in two libraries also produces two identically named public types visible to a consumer.

**Current behaviour, 1.7.5**
- `src/ZeroAlloc.Validation.Generator/ValidatorGenerator.cs:345`: the class header is always `public sealed partial class`.
- `src/ZeroAlloc.Validation.Options.Generator/OptionsValidationEmitter.cs:44`: `public static class ZeroAllocOptionsValidationExtensions`, with no namespace.
- Probe with an internal `[Validate]` options class:
  ```
  JevOptionsValidator.g.cs(11,29): error CS9338: Inconsistent accessibility: type 'JevOptions' is less accessible than class 'JevOptionsValidator'
  JevOptionsValidator.g.cs(14,67): error CS0051: Inconsistent accessibility: parameter type 'JevOptions' is less accessible than method 'JevOptionsValidator.Validate(JevOptions)'
  ```

**Proposed API and behaviour**
1. Emit validators with the model's effective accessibility by default. This fixes the compile error.
2. Add an opt-in to keep validators internal for public models:
   ```csharp
   [Validate(ValidatorAccessibility = GeneratedAccessibility.Internal)]
   public sealed class JevClientOptions { }
   ```
   or an assembly-level default, such as `[assembly: ZeroAllocValidation(InternalValidators = true)]`.
3. Emit the options extension class `internal` when any bound model or validator is internal, and place it in a namespace. The model's namespace, or `Microsoft.Extensions.DependencyInjection`, would both work.

**Alternatives considered**
Accept the public validators and list them in `PublicAPI.Shipped.txt`. That commits a library to shipping generated types forever as public API.

**Compatibility**
Point 1 turns a compile error into working code. Point 2 is opt-in. Point 3 changes accessibility only where it is currently inconsistent, or adds a namespace, and the namespace change needs a changelog note.

---

### I1: ZeroAlloc.Inject. Option to emit the generated registration method as `internal`

**Filed:** [ZeroAlloc.Inject#156](https://github.com/ZeroAlloc-Net/ZeroAlloc.Inject/issues/156)

**Title:** Allow the generated `Add{Assembly}Services()` extension to be `internal` for library authors

**Body:**

**Summary**
The MS DI extension-mode generator always emits `public static class {Name}ServiceCollectionExtensions`, with a `public` `Add{Assembly}Services()`, into `Microsoft.Extensions.DependencyInjection`. There is no way to make it internal. In a library this adds an unintended public entry point that registers internal services.

**Motivation**
Jev.Net ships `Jev.Net.Extensions.DependencyInjection`, whose only intended public API is a hand-written `services.AddJevClient(...)`. It wants to use ZeroAlloc.Inject attributes for its internal services and call the generated method from inside `AddJevClient`. Today that also publishes `AddJevNetExtensionsDependencyInjectionServices()` to every consumer. PublicApiAnalyzers flags it, and consumers see a method they should never call directly.

**Current behaviour, ZeroAlloc.Inject 1.7.6**
- `src/ZeroAlloc.Inject.Generator/ZeroAllocInjectGenerator.cs:756-758`: `"    public static class " + className` and `"        public static IServiceCollection " + methodName + "(this IServiceCollection services)"`, with no accessibility input.
- `src/ZeroAlloc.Inject/ZeroAllocInjectAttribute.cs`: the assembly attribute only takes `MethodName`.
- Probe: an assembly named `Jev.Net.Extensions.DependencyInjection` containing one `internal` `[Singleton]` class generates:
  ```
  namespace Microsoft.Extensions.DependencyInjection
      public static class JevNetExtensionsDependencyInjectionServicesServiceCollectionExtensions
          public static IServiceCollection AddJevNetExtensionsDependencyInjectionServices(this IServiceCollection services)
  ```

**Proposed API**

```csharp
[assembly: ZeroAllocInject("AddJevNetServices", Accessibility = GeneratedAccessibility.Internal)]
```

When `Internal` is set, emit `internal static class`. Alternatively, and automatically, emit `internal` when every registered service type and implementation is internal. The explicit property is clearer, and it covers public services too.

**Alternatives considered**
- Skip ZeroAlloc.Inject in libraries and register by hand. That loses the compile-time registration checks.
- Accept the public method and document it as unsupported. It remains part of the shipped API forever.

**Compatibility**
Additive. The default stays `public`.

---

### X1: ZeroAlloc.TestHelpers. `GeneratorSnapshot.cs` breaks every consumer that does not reference Roslyn

**Filed:** [ZeroAlloc.TestHelpers#50](https://github.com/ZeroAlloc-Net/ZeroAlloc.TestHelpers/issues/50)

**Title:** 1.3.x compiles `GeneratorSnapshot.cs` into every consumer, which fails without a Microsoft.CodeAnalysis reference

**Body:**

**Summary**
The package distributes two source files as `contentFiles` with `buildAction="Compile"`: `AllocationGate.cs` and `GeneratorSnapshot.cs`. `GeneratorSnapshot.cs` uses `Microsoft.CodeAnalysis`. Any project that references the package for `AllocationGate` alone, such as a test project or a Native AOT smoke app with no Roslyn reference, fails to compile.

**Motivation**
Jev.Net gates allocation budgets with `AllocationGate` in its unit tests and in its Native AOT smoke app. Neither references Roslyn. The AOT smoke app should not pull in the compiler just to compile an unused helper.

**Current behaviour, 1.3.2**
- The nuspec contains `<files include="cs/any/ZeroAlloc.TestHelpers/GeneratorSnapshot.cs" buildAction="Compile" />`.
- Probe: a console app referencing the package with `IncludeAssets="contentfiles;build"`, as the README shows:
  ```
  GeneratorSnapshot.cs(8,17): error CS0234: The type or namespace name 'CodeAnalysis' does not exist in the namespace 'Microsoft'
  GeneratorSnapshot.cs(44,9): error CS0246: The type or namespace name 'GeneratorDriver' could not be found
  GeneratorSnapshot.cs(58,9): error CS0246: The type or namespace name 'GeneratorDriverRunResult' could not be found
  ```
- Workaround used in the probe: a target that removes the `GeneratorSnapshot` item from `Compile` before `CoreCompile`. With it, `AllocationGate.AssertBudgetValueTask` works as documented.

**Proposed fix**
Any one of the following:
1. Wrap `GeneratorSnapshot.cs` in `#if ZEROALLOC_TESTHELPERS_GENERATOR_SNAPSHOT`, and have a `build/ZeroAlloc.TestHelpers.targets` define the symbol when a `Microsoft.CodeAnalysis.CSharp` reference is present, or when the consumer opts in with a property.
2. Ship an MSBuild property such as `ZeroAllocTestHelpersIncludeGeneratorSnapshot`, defaulting to false, that removes the file from `Compile` unless set.
3. Split it into two packages: `ZeroAlloc.TestHelpers` for `AllocationGate` only, and `ZeroAlloc.TestHelpers.Generators` for `GeneratorSnapshot`.

Update the README either way. It currently describes a single file, `AllocationGate.cs`.

**Alternatives considered**
Require every consumer to reference Roslyn. That is heavy for AOT smoke apps and irrelevant to allocation gating.

**Compatibility**
Options 1 and 2 keep existing generator-test consumers working, because they already reference Roslyn. Option 3 needs those repos to add the new package.

---

## 3. Supported items: how Jev.Net should use them

**Transport, phase 1.4, ZeroAlloc.Rest 1.3.5**
- Declare `[ZeroAllocRestClient] internal interface IJevApi` with `[Post("v1/systemone")] ValueTask<Result<SystemOneResponse, HttpError>> AskAsync([Body] SystemOneRequest body, CancellationToken ct)` and `[Get("v1/models")] ValueTask<Result<ModelList, HttpError>> ListModelsAsync(CancellationToken ct)`. Use relative routes without a leading slash, and a base address with a trailing slash: `https://api.typesafe.ai/`. The generator emits an internal `JevApiClient` and an internal `InternalGeneratedRestClientExtensions.AddIJevApi`.
- The DTOs can stay as they are. Internal DTOs also compile with 1.3.5.
- The generated class does not repeat the interface's default parameter values, so `ct = default` is only available through the interface. Always call through `IJevApi`.
- Serializer: construct `new SystemTextJsonSerializer(JevJson.Options)` explicitly. Do not use `UseSerializer<SystemTextJsonSerializer>()`: DI would pick the parameterless constructor, which gives Web defaults and reflection metadata. Until R4 lands, a library-safe option is to construct `JevApiClient` from Jev.Net's own factory, passing the `HttpClient` from `IHttpClientFactory` and Jev.Net's serializer instance, instead of relying on the container-wide `IRestSerializer`.
- AOT: no custom `IRestSerializer` is needed to meet the zero-warning DoD. Probe a published native AOT with 0 warnings under TWAE and ran correctly. If Jev.Net ever writes its own `IRestSerializer`, for example to use `JsonTypeInfo` overloads, its methods must repeat the interface's `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` to avoid IL2046. Probe g built clean that way.
- Auth: set `Authorization` once per `HttpClient`, through `ConfigureHttpClient` on the builder or a `DelegatingHandler` that reads the key resolved from `TYPESAFE_API_KEY`. A method-level `[Header("Authorization")]` parameter also works, but it would put the key into every call signature.
- The generated client already emits `ZeroAlloc.Rest` ActivitySource spans and the `rest.requests_total` and `rest.request_duration_ms` metrics, so phase 3.3 need not duplicate HTTP latency.

**Error model, phase 1.4**
- Status code: `HttpError.StatusCode`. It can be cast to int for 529.
- `Retry-After`: `HttpError.Headers["Retry-After"]`. Probe a confirmed it is present for a response header. Parse both the delta-seconds and HTTP-date forms.
- Until R1, R2 and R3 land: wrap each generated call in a small internal adapter that catches `HttpRequestException`, `TaskCanceledException` when the caller's token is not cancelled, and `JsonException`, and maps them to `JevError`. For the 422 body, either add a `DelegatingHandler` that buffers error bodies, or accept a body-less `JevError.Validation` for now.
- Use `ZeroAlloc.Results` `MapError`, `Bind`, `Match` and `MapAsync` to convert `Result<T, HttpError>` to `Result<T, JevError>`.

**Resilience, phase 1.5**
- The ZeroAlloc.Resilience generator cannot be used on the transport interface today: see S1, S2, S3 and S5. Plan a hand-written retry loop in the adapter above for 429 and 529 only, with exponential backoff, optional jitter and `Retry-After`, capped at a maximum delay. Revisit once S1 to S5 ship.

**Telemetry, phase 3.3**
- `[Instrument("Jev.Net")]` on an internal interface gives an internal proxy by default. Use `[Trace]`, `[TraceTag]` for request arguments, `[TraceTagConstant]`, and `[TraceTagFromResult("jev.model", "Value.Model", When = "IsSuccess")]`. Token and confidence metrics need hand-written `Meter` code until T1 lands.
- Per-answer confidence lives in a dictionary of polymorphic answers, which no attribute can iterate. Even with T1, Jev.Net needs either a computed value or a hand-written loop.

**DI and options, phases 3.1 and 3.2**
- The typed client uses the generated `AddIJevApi`, or `AddHttpClient<IJevApi, JevApiClient>`, which returns an `IHttpClientBuilder` for handlers and `ConfigurePrimaryHttpMessageHandler`.
- ZeroAlloc.Inject: until I1, register by hand in `AddJevClient`, or accept and list the generated public method.
- ZeroAlloc.Validation.Options: until V1, register `IValidateOptions<JevClientOptions>` by hand with `ZeroAllocOptionsValidator<JevClientOptions>` and the generated validator. Until V2, the validator for a public options type is public, so list it in the PublicAPI files or validate by hand.

**Benchmarks and AOT smoke, phase 1.7**
- `AllocationGate.AssertBudget` and `AllocationGate.AssertBudgetValueTask` work. The generated client over an in-memory `HttpMessageHandler` completes synchronously, which `AssertBudgetValueTask` requires. Until X1 lands, add a target that removes the `GeneratorSnapshot` item from `Compile`.

## 4. Could not determine

- **Whether TypeSafe sends `Retry-After`.** The API docs at `api.md:325-338` say to back off exponentially on 429 and 529 and never mention a `Retry-After` header. No live call was made, because no API key was used. Jev.Net should honour the header if present and fall back otherwise.
- **The JSON shape of error bodies.** The docs say the 422 body "details the offending field" but give no schema, so `JevError.Validation`'s payload shape stays open.
- **Linux AOT.** Native AOT was verified on win-x64 only. ILC's trim and AOT analysis does not depend on the platform, but the CI target was not run.
- **ZeroAlloc.Inject with typed `HttpClient`s.** Inject has no attribute for typed-HttpClient registration; ZeroAlloc.Rest's `AddHttpClient` covers that. Whether Inject is needed at all in phase 3.1 is a design question. Only its public-API leak was checked.
- **ZeroAlloc.Rest.Resilience at runtime.** It could not be exercised, because every Result-returning configuration failed to compile in the Resilience generator. Its `AddRestResilience` code path was only read.
- **Allocation figures.** The probes confirmed that `AllocationGate` is usable. They did not measure the per-call bytes of the generated client. That belongs in phase 1.7's baseline.
