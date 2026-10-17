---
id: datasets
title: Labelled datasets
sidebar_position: 7
description: The JSONL format for labelled decision items, the members of a line, how the loader reports problems, and the format version.
---

# Labelled datasets

A labelled dataset is a file of items with the right answers written next to them. Each item is a state to decide on,
the questions to ask about it and the answer you expect to every question. You can run the items through any
`IDecisionClient` and compare what comes back with what you expected.

The `Minos.NET.Evaluation` project reads the format. `DecisionDataset` loads a file, a stream or a string, and gives
back `DatasetItem` values that carry everything a call needs: the state, the question set and the expected answers.

## The format

A dataset is UTF-8 JSONL: one item per line, and blank lines are ignored. Every line is complete in itself. It holds the
state, the questions and the expected answers, so the questions repeat on each line, and you can cut, shuffle or
concatenate a file without losing anything.

The example is one line. It is shown on a single line because JSONL needs that.

```json
{"id":"msg-1","state":"My card was meant to arrive last week and nothing has come.","questions":{"topic":{"type":"choice","instructions":"Which topic is this message about?","criteria":{"card_arrival":"The card has not arrived yet.","card_not_working":"The card arrived but does not work.","lost_or_stolen_card":null}},"urgent":{"type":"noul","instructions":"Does the customer need action today?"},"mood":{"type":"score","instructions":"How upset is the customer?","criteria":["calm","annoyed","angry"]}},"expected":{"topic":"card_arrival","urgent":false,"mood":1},"source":"support/2026-10#1","version":1}
```

## The members

| Member | Required | Content |
| --- | --- | --- |
| `id` | yes | A non-empty string, unique within the file. |
| `state` | yes | A string, or a JSON object or array for structured state. A number, boolean or `null` is a problem. |
| `questions` | yes | An object of questions keyed by question key, at least one. |
| `expected` | yes | The right answer for each question key, described below. Every key must name a question. A question with no expected answer is still sent, but it is not scored. |
| `source` | no | A string of free text that names where the item came from. |
| `version` | no | The format version. When it is present it must be `1`. |

Any other member is a problem. A typo such as `expect` is reported instead of being dropped without a word. A member
that appears twice in the same object is a problem too.

### Questions

Each question is an object with these members:

- `type` is `noul`, `choice` or `score`.
- `instructions` is a string, or a JSON object or array for structured instructions.
- `criteria` depends on the kind:
  - A Noul may have `{"true": ..., "false": ...}`, with either side left out. Each side is a string, or a JSON object
    or array, and it is read as content. It is not a criterion object.
  - A Choice needs an object of options, where each description is `null`, a string or a criterion object.
  - A Score needs an ordered list of levels, where each level is a string or a criterion object.

A criterion object, for a Choice option or a Score level, has the members `description`, `examples` and `not_for`.
`description` is required and is a string. `examples` and `not_for` are optional arrays of strings: the examples of the
option, and the things the option is not for. A criterion object holds text only. A JSON object or array as the
description of a Choice option or a Score level is not part of version 1, although the sides of a Noul may be JSON
values, as above.

The questions go through the same checks as a question set built at run time, so the rules of
[Question sets at run time](question-sets-at-run-time.md) apply, with the same `MIN` ids.

### Expected answers

The expected value follows the kind of question:

- A Noul takes a JSON boolean, `true` or `false`.
- A Choice takes the key of an option, as a string. The key must be one of the options.
- A Score takes the index of a level as an integer, counted from 0. The index must be inside the list of levels.

In code an expected answer is an `ExpectedAnswer`, made with `ExpectedAnswer.ForNoul`, `ExpectedAnswer.ForChoice` or
`ExpectedAnswer.ForScore`. It compares by value.

## Problems are values

A file with bad content does not throw. `DecisionDataset.Parse` and `DecisionDataset.Load` return a result that holds
either the dataset or the full list of problems, so you fix a file in one pass instead of one error at a time. The list
is a `DatasetProblems`, and each `DatasetProblem` has a 1-based `Line` and a `Message`.

A problem the core finds in a question starts with its `MIN` id, for example `MIN003: ...`. The core's warnings count
as problems here too, all of them, because an item that breaks one is a broken item. MIN003 reports blank text, such as
blank instructions or a blank description. MIN005 reports a Score with fewer than two levels or more than 10, and a
Choice with more than 255 options.

The loader throws only when the file does not exist or cannot be read.

## The version

The `version` member is optional. A line without it is read as version 1. A whole number other than 1 is a problem
that says: "This file uses dataset format version N; this loader reads version 1." Any other value, such as `"one"` or
`1.0`, is a problem that says: "'version' must be the number 1." A later version of the format will raise the number,
and a loader that does not know it refuses the file instead of guessing.

The decision export planned for Milestone 9 will write this format, and will use a new version if it needs members that
version 1 does not have.

## Next

- [Question sets at run time](question-sets-at-run-time.md): the builder and the rules every question goes through.
- [The client and its errors](client-and-errors.md): the `IDecisionClient` that the items run through.
- [Question types](question-types.md): what a Noul, a Choice and a Score answer holds.
