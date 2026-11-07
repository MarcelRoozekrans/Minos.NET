---
id: extensions-ai-decisions
title: Microsoft.Extensions.AI decisions
sidebar_position: 1
description: How Minos relates to the experimental decision abstractions proposed for Microsoft.Extensions.AI, which commit the comparison was checked against, and what the adapter package does.
---

# Microsoft.Extensions.AI decisions

Microsoft.Extensions.AI has a proposal for decision abstractions: an `IDecisionClient` that answers binary, choice and
score questions with probabilities. It is tracked in
[dotnet/extensions issue 7764](https://github.com/dotnet/extensions/issues/7764), and the code is in
[pull request 7795](https://github.com/dotnet/extensions/pull/7795). Minos has its own `IDecisionClient` with the same
three question kinds. These pages compare the two.

## Status of the upstream API

The upstream API is experimental and unreleased. Its types carry the `MEAI001` experimental diagnostic, and no NuGet
package contains them yet. It can change shape before it ships, or not ship at all.

So Minos compares against one commit. The file
[`tools/upstream/decisions-pin.json`](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/tools/upstream/decisions-pin.json)
names the repository, `dotnet/extensions`, and the commit `02e54f023bdf3c8c2729353b110c22f5f8593957`, with the date it
was checked. The script `tools/upstream/build-decisions.sh` clones that commit and builds
`Microsoft.Extensions.AI.Abstractions` from it. A test in the docs test project loads that build and fails when the
mapping page names an upstream type or member it does not have. Moving to a newer commit is a deliberate change to the pin
file, and the mapping is checked again when it moves.

## The adapter package

`Minos.NET.Extensions.AI.Decisions` holds two adapters:

- a Minos `IDecisionClient` over an upstream client, so the Minos pipeline (retry, escalation, fallback, caching,
  recording) can run over any upstream provider;
- an upstream `IDecisionClient` over a Minos client, so code written against the upstream API can use a Minos provider or
  a whole Minos pipeline.

The adapters are in the repository and built, but not published until Phase 11.3, which waits for a released upstream
package. They build only against the upstream assembly from `tools/upstream/build-decisions.sh`, in the `Decisions`
configuration; [the project README](https://github.com/MarcelRoozekrans/Minos.NET/blob/main/src/Minos.NET.Extensions.AI.Decisions/README.md) gives the commands.

It is a different package from `Minos.NET.Extensions.AI`, which asks a Microsoft.Extensions.AI chat client
(`IChatClient`) decision questions. [Chat clients](../chat-clients.md) describes that one.

## What the mapping covers

[The mapping](mapping.md) has one table per area: the client interface, the request and its options, the question
kinds, the answers and their distributions, errors, metadata and capabilities, and the parts of Minos with no upstream
counterpart. Each row says whether the two sides map one to one, map with a loss, or have no counterpart, and what an
adapter does about it.

## Next

- [The mapping](mapping.md): every Minos decision type next to its upstream counterpart.
- [The client and its errors](../client-and-errors.md): the Minos client interface the adapters implement.
- [Question types](../question-types.md): the Minos question kinds the mapping starts from.
