using System.Diagnostics;
using Minos.Recording;
using ZeroAlloc.Results;

namespace Minos.Evaluation.Tests;

/// <summary>
/// The real recorder and JSONL sink record decisions, a candidate shadows them, and the shadow report reads the file the
/// recorder wrote.
/// </summary>
public sealed class ShadowEndToEndTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 9, 35, 0, TimeSpan.Zero);
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private static DecisionRequest Request(string state)
    {
        var parsed = DecisionDataset.Parse("{\"id\":\"seed\",\"state\":\"" + state + "\",\"questions\":{\"urgent\":{\"type\":\"noul\",\"instructions\":\"Does this convey urgency?\"}},\"expected\":{\"urgent\":true}}");
        Assert.True(parsed.IsSuccess);
        return new DecisionRequest(Assert.Single(parsed.Value).Definition, Assert.Single(parsed.Value).State);
    }

    private static Result<DecisionResponse, DecisionError> Answer(DecisionRequest request, double probability, string model)
        => Result<DecisionResponse, DecisionError>.Success(new DecisionResponse(request.Definition, [QuestionAnswer.Noul(probability)], model));

    [Fact]
    public async Task RecordShadowAndReport_EndToEnd()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-shadow-e2e-" + Guid.NewGuid().ToString("N") + ".jsonl");
        try
        {
            var primary = new FakeDecisionClient((r, _) => ValueTask.FromResult(Answer(r, 0.9, "primary-model")));

            // The candidate answers no for "d3" only, and holds "d5" open until the test lets it go.
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var held = 0;
            var candidate = new FakeDecisionClient(
                async (r, ct) =>
                {
                    Assert.True(r.State.TryGetString(out var state));
                    if (state!.Contains("d5", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref held);
                        await release.Task.WaitAsync(ct);
                    }

                    return Answer(r, state.Contains("d3", StringComparison.Ordinal) ? 0.1 : 0.9, "candidate-model");
                },
                new DecisionClientMetadata("candidate", new Uri("https://candidate.example.test/"), "candidate-default"));

            var options = new RecordingOptions { Capacity = 64, FlushTimeout = TimeSpan.FromSeconds(10), Redact = static state => RedactionResult.Keep(state) };
            var recorder = new DecisionRecorder(new JsonlDecisionRecordSink(path), options);
            var shadow = new ShadowOptions(candidate, dailyCap: 100) { MaxConcurrent = 8 };
            var confirmed = new Dictionary<string, ConfirmedAnswer>(StringComparer.Ordinal) { ["urgent"] = ConfirmedAnswer.ForNoul(true) };
            try
            {
                using var client = primary.AsBuilder().UseRecording(shadow, recorder).Build();
                foreach (var id in new[] { "d1", "d2", "d3", "d4", "d5" })
                {
                    using var scope = DecisionRecording.Begin(decisionId: id);
                    Assert.True((await client.EvaluateAsync(Request("state " + id))).IsSuccess);
                }

                foreach (var id in new[] { "d1", "d2", "d3", "d4" })
                {
                    await recorder.AddOutcomeAsync(id, DecisionOutcome.Confirmed("reviewer-ui", At, confirmed));
                }

                // The erasure is handled while the fifth candidate still runs, so its late write is dropped.
                var waiting = Stopwatch.StartNew();
                while (Volatile.Read(ref held) == 0)
                {
                    Assert.True(waiting.Elapsed < Bound, "The fifth candidate did not start.");
                    await Task.Delay(5);
                }

                await recorder.EraseDecisionAsync("d5");
                release.SetResult();

                // The four other candidates write as they finish; flush until the file holds all four.
                var deadline = Stopwatch.StartNew();
                while (candidate.Requests.Count < 5 || await ShadowedAsync(recorder, path) < 4)
                {
                    Assert.True(deadline.Elapsed < Bound, "The candidates' writes did not reach the file.");
                    await Task.Delay(5);
                }

                await recorder.FlushAsync();
            }
            finally
            {
                await recorder.DisposeAsync();
            }

            var report = await ShadowReport.CreateAsync(DecisionRecordFile.ReadAsync(path, CancellationToken.None));

            // The erased decision is absent, so its late candidate answer did not bring it back.
            Assert.Equal(4, report.DecisionsRead);
            Assert.Equal(4, report.DecisionsShadowed);
            var question = Assert.Single(report.Questions);
            Assert.Equal("urgent", question.Key);
            Assert.Equal(4, question.Compared);
            Assert.Equal(0.75, question.Agreement);
            Assert.Equal(4, report.Primary.CalibratedKeys);
            Assert.Equal(4, report.Candidate.CalibratedKeys);
            Assert.DoesNotContain("d5", report.ToJson(), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Flushes the recorder, then counts the decisions in the file that hold the candidate's answers.
    private static async Task<int> ShadowedAsync(DecisionRecorder recorder, string path)
    {
        await recorder.FlushAsync();
        var shadowed = 0;
        await foreach (var decision in DecisionRecordFile.ReadAsync(path, CancellationToken.None))
        {
            if (decision.Record.Shadow is not null)
            {
                shadowed++;
            }
        }

        return shadowed;
    }
}
