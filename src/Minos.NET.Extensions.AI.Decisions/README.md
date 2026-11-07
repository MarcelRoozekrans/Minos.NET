# Minos.NET.Extensions.AI.Decisions

Adapters between Minos.NET decision clients and the experimental decision abstractions in `dotnet/extensions`
(`IDecisionClient` in `Microsoft.Extensions.AI.Abstractions`).

This project is not packed. It follows an unreleased upstream API, so every public type carries the upstream
experimental marker `MEAI001` and can change or disappear with the upstream types. It is built only in the `Decisions`
solution configuration, against an upstream assembly built locally:

```bash
bash tools/upstream/build-decisions.sh
dotnet build Minos.NET.slnx -c Decisions
dotnet test Minos.NET.slnx -c Decisions
```

The default `Release` build and test never need the upstream clone. See `tools/upstream/README.md` for the pin.
