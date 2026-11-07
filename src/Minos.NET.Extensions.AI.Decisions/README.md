# Minos.NET.Extensions.AI.Decisions

Adapters between Minos.NET decision clients and the experimental decision abstractions in `dotnet/extensions`
(`IDecisionClient` in `Microsoft.Extensions.AI.Abstractions`).

Each entry point is named after the client it returns:

- `minosClient.AsUpstreamDecisionClient()` returns a `MinosDecisionClient`, an upstream `IDecisionClient` that asks the
  Minos client or pipeline.
- `upstreamClient.AsMinosDecisionClient()` returns an `UpstreamDecisionClient`, a Minos `IDecisionClient` that asks the
  upstream client.

Each adapter owns the client it wraps and disposes it. `docs/extensions-ai-decisions/mapping.md` describes every
conversion.

This project is not packed. It follows an unreleased upstream API, so every public type carries the upstream
experimental marker `MEAI001` and can change or disappear with the upstream types. It is built only in the `Decisions`
solution configuration, against an upstream assembly built locally:

```bash
bash tools/upstream/build-decisions.sh
dotnet build Minos.NET.slnx -c Decisions
dotnet test Minos.NET.slnx -c Decisions
```

The default `Release` build and test never need the upstream clone. See `tools/upstream/README.md` for the pin.

## Trim and AOT

The project sets `IsAotCompatible`, which enables the trim, AOT and single-file analyzers, and the repository builds with
warnings as errors, so any IL2xxx or IL3xxx diagnostic fails the build. The `Decisions` build is therefore the check:
`dotnet build Minos.NET.slnx -c Decisions`, which the `upstream-decisions` job runs. It was verified to fail on a
reflection call (IL2057 and IL2067).

Not covered yet: a `PublishAot` publish of an app that calls both adapters, as `aot-smoke` and `aot-surface` do for the
packed libraries. The upstream assembly cannot be pulled into the default build, and those jobs run in `Release`. The
analyzer check covers the adapters' own code, not the upstream assembly's trim behaviour.
