# Jev.Net

Unofficial .NET client for [TypeSafe AI](https://typesafe.ai)'s **Jev**, the first System One model: send a `state` and typed questions (Noul, Choice, Score) and get calibrated, typed answers back.

> **Not affiliated with TypeSafe AI.** Jev.Net is a community project. TypeSafe publishes official SDKs for Python and JavaScript; see [docs.typesafe.ai](https://docs.typesafe.ai).

**Status:** early development — not yet usable. See [the roadmap](docs/planning/ROADMAP.md).

## Goals

- Source-generated and Native AOT-compatible, built on the [ZeroAlloc](https://github.com/ZeroAlloc-Net) ecosystem
- Strongly typed questions and answers
- `Result<T, E>`-based error handling
- Benchmarked and allocation-budgeted

## License

[MIT](LICENSE)
