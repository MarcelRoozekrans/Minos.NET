# Provider-switch sample

This sample routes eight support tickets to a team, billing, technical, account or sales, with one Choice question per
ticket. It runs against TypeSafe, OpenRouter or a decision server on your own machine, and only the configuration
changes between them. It shows three things:

- **One section per provider.** `appsettings.json` holds a section for each provider under `Minos:Providers`, and
  `Minos:Active` names the one to use. The program binds the active section with `AddSystemOneClient`, and no other
  code knows which provider answers.
- **The same request, different answers.** The report prints each ticket's team and confidence, then the provider and
  the model that answered, so two runs can be compared line by line.
- **One recording per provider.** TypeSafe and OpenRouter receive byte-identical requests, because both ask
  `jev-latest`, so each provider's answers are kept in a file of their own.

The eight tickets are written for the sample.

## Run it

```
dotnet run --project samples/Minos.NET.Samples.ProviderSwitch
```

By default the sample replays the provider that `Minos:Active` names, `TypeSafe`, from `recordings/typesafe.json`. It
then needs no network and no key. Pick another provider with `--provider`, or
with the environment variable `Minos__Active`:

```
dotnet run --project samples/Minos.NET.Samples.ProviderSwitch -- --provider openrouter
dotnet run --project samples/Minos.NET.Samples.ProviderSwitch -- --provider local
```

An unknown provider, or none at all, exits with code 2 and lists the configured providers. Replay of a provider that
has no recordings file exits with code 1 and prints the command that records it.

To call a provider instead, pass `--live` or `--record` with it:

- TypeSafe needs `TYPESAFE_API_KEY`, and OpenRouter needs `OPENROUTER_API_KEY`. Each live run makes eight billed
  requests.
- The local server needs no key, but it must be running first, at `http://localhost:8000/`. Without it the run fails
  with the client's network error. The
  [Providers page](https://marcelroozekrans.github.io/Minos.NET/providers#running-a-local-server) has the commands
  that start it.

`--record` rewrites that provider's file in `recordings/`, in the sample's source folder, so run it from a clone.

## The recordings

`recordings/` holds one file per provider: `typesafe.json`, `openrouter.json` and `local.json`. The billed Live smoke
workflow records the first two, and the free Local systemone workflow records `local.json`. Each run uploads the files
as an artifact, and they reach the repository through a pull request. All three were recorded on 2026-10-12:
`typesafe.json` from the model `jev-1.13.0`, `openrouter.json` from `typesafe/jev-1.13-20260917`, and `local.json` from
`Mapika/decider-2b-GGUF:Q4_K_M`.
