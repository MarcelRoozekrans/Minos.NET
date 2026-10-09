# Re-ranking sample

This sample answers customer questions for a fictional bike-sharing app, Pedalo. A cheap keyword search finds eight candidate help articles out of 25, and Jev then re-orders those eight by how well each one answers the question. It shows three things:

- **Every candidate is scored in one fan-out request.** For each question the sample asks one yes/no question per candidate, "does this article answer the customer's question?", and sends all eight in a single Jev request. The customer's message is sent once, as the state all eight questions are asked about. The probability of each answer is the score, and the highest score goes first. Five questions cost five requests.
- **The question set is built at run time.** The candidates differ for every customer question, so there is no fixed set of questions to declare on a class. The sample uses the builder API: it adds one Noul per candidate, keyed by the article id, and reads each answer back through the handle it got when it added the question.
- **The keyword shortlist is deliberately simple.** It lower-cases the text, splits it on anything that is not a letter or a digit, drops words shorter than three characters and a few stop words, and counts how many distinct query words appear in an article's title and body. It is quick and it often misses, which is the point: it only has to get the right article somewhere into the top eight.

The 25 articles and the five questions are written for the sample. Each question has one article a person would pick as its best answer, and for most of them a weaker article shares more of its words than the best one does.

## Run it

```
dotnet run --project samples/Minos.NET.Samples.Reranking
```

By default the sample replays the answers checked in as `recordings.json`. It needs no network and no key.

Replay and record read and write the recordings in your clone of the repository, so run those two modes from a clone. `--live` reads no recordings and runs from anywhere.

To call the API instead, set `OPENROUTER_API_KEY` and pass a mode after `--`:

- `-- --live` sends the requests and prints the report.
- `-- --record` does the same and then rewrites `recordings.json` from the responses. It writes straight to the sample's source folder, and replay reads the same file, so a fresh recording shows on the next run.

The live modes make 5 billed requests.

## The recordings

`recordings.json` was recorded through OpenRouter with the model `typesafe/jev-1.13-20260917` on 2026-10-02. Record again whenever the articles, the questions or the shortlist size change, because they all change the requests.

## The result

Treat the numbers as the shape of the gain, not as a benchmark. The corpus is small, the shortlist is 8 of 25 articles, and the keyword retriever is deliberately weak.

In the recorded run the keyword order put the best article first for 1 of the 5 questions and in the top three for 2 of 5. After re-ranking, the best article was first for all 5.

```
hit@1 keyword 1/5 -> jev 5/5
hit@3 keyword 2/5 -> jev 5/5
```

## Things to know

Re-ranking can only reorder what the shortlist found. If the best article is not among the eight candidates, Jev never sees it, so the shortlist has to be wide enough for the recall you need.

The probabilities are not spread evenly. For one question the best article scored only 0.29, far lower than the 0.72 to 0.94 of the best articles for the other questions, yet it still came first because every other candidate scored 0.02 or less. Rank by the order, and do not treat a fixed probability as the threshold for "this answers it".

Candidates with equal probabilities keep their keyword order, so the result is the same on every run.

## Credit

Inspired by TypeSafe's [Re-ranking](https://docs.typesafe.ai/cookbooks/rerank_typesafe) cookbook; the articles, queries and code here are our own.
