---
id: patterns
title: Patterns
sidebar_position: 1
description: Four ways to use Jev answers, each with a guide of its own.
---

# Patterns

Four ways to use Jev's answers, each with a page of its own.

- [Speculative fan-out](fan-out.md): ask every question you might need in one request, and read only what matters.
- [Confidence routing](confidence-routing.md): gate each action on the answer's confidence, with a threshold sized to
  the cost of being wrong.
- [Composite scoring](composite-scoring.md): break a judgement into small Scores and combine them with weights you own.
- [Intent routing](intent-routing.md): make a cheap first decision that sends each request to code, a model or a person.

The four combine. Fan-out supplies the answers, and routing and scoring read them: one request can carry the intent,
the effort and the atomic scores, and your code then applies the gates and the weights.

The C# on every page is compiled and tested in `tests/Minos.NET.Docs.Tests`.

## Next

- [Speculative fan-out](fan-out.md): the first pattern, asking every question you might need in one request.
- [Samples](../samples.md): three whole programs that use these patterns, run offline from recorded answers.
- [Testing your code](../testing-your-code.md): test code that uses Minos with canned answers.
