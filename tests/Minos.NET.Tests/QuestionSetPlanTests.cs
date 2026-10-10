using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>What a built set keeps for reading answers: the plan, the keys, the offsets and the handle indexes.</summary>
public sealed class QuestionSetPlanTests
{
    [Fact]
    public void MixedSet_PlansEachQuestionInWireOrder()
    {
        var builder = QuestionSet.CreateBuilder()
            .Noul("urgent", "Urgent?", out var noul)
            .Choice<Department>("team", "Which team?", out var team)
            .Score<Frustration>("mood", "How?", out var mood, l => l
                .Level(Frustration.VeryAngry, "Very angry")
                .Level(Frustration.Calm, "Calm")
                .Level(Frustration.Frustrated, "Frustrated"))
            .Choice("product", "Which product?", out var product, o => o.Option("pro-plan").Option("team-plan", "The Team subscription"))
            .Score("effort", "Effort?", out var effort, l => l.Level("Minutes").Level("Hours").Level("Days"));

        var set = builder.Build().Value;
        var again = builder.Build().Value;

        Assert.Same(set.Identity, again.Identity);
        Assert.Equal([0, 1, 2, 3, 4], new[] { noul.Index, team.Index, mood.Index, product.Index, effort.Index });
        Assert.All(new[] { noul.Owner, team.Owner, mood.Owner, product.Owner, effort.Owner }, s => Assert.Same(set.Identity, s));

        QuestionKind[] kinds = [QuestionKind.Noul, QuestionKind.Choice, QuestionKind.Score, QuestionKind.Choice, QuestionKind.Score];
        string[] keys = ["urgent", "team", "mood", "product", "effort"];
        int[] offsets = [0, 0, 4, 7, 9];
        Assert.Equal(kinds.Length, set.Plan.Length);
        Assert.Equal(12, set.Definition.ProbabilityCount);
        Assert.Equal(keys.Select(k => Encoding.UTF8.GetBytes(k)), set.Definition.KeysUtf8);

        using var document = JsonDocument.Parse(set.QuestionsUtf8.ToArray());
        var wire = document.RootElement.EnumerateObject().ToArray();
        Assert.Equal(keys, wire.Select(p => p.Name));

        for (var i = 0; i < kinds.Length; i++)
        {
            var plan = set.Plan[i];
            Assert.Equal(kinds[i], plan.Kind);
            Assert.Equal(keys[i], plan.Key);
            Assert.Equal(offsets[i], plan.Offset);

            if (kinds[i] == QuestionKind.Noul)
            {
                Assert.Null(plan.Options);
                continue;
            }

            var criteria = wire[i].Value.GetProperty("criteria");
            var wireKeys = WireKeys(criteria);

            Assert.NotNull(plan.Options);
            Assert.Equal(wireKeys.Length, plan.Options.Count);
            for (var n = 0; n < wireKeys.Length; n++)
            {
                Assert.Equal(n, IndexOfKey(plan.Options, wireKeys[n]));
            }
        }
    }

    private static string[] WireKeys(JsonElement criteria)
        => criteria.ValueKind == JsonValueKind.Array
            ? Enumerable.Range(0, criteria.GetArrayLength()).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()
            : criteria.EnumerateObject().Select(p => p.Name).ToArray();

    private static int IndexOfKey(IDecisionOptionKeys options, string key)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(key)));
        Assert.True(reader.Read());
        return options.IndexOfKey(ref reader);
    }
}
