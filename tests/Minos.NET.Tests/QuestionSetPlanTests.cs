using System.Globalization;
using System.Text;
using System.Text.Json;
using Minos.Protocols;

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
        Type?[] optionSets = [null, typeof(EnumOptionSet<Department>), typeof(EnumOptionSet<Frustration>), typeof(KeyedOptionSet), typeof(KeyedOptionSet)];
        Assert.Equal(kinds.Length, set.Plan.Length);
        Assert.Equal(12, set.Definition.ProbabilityCount);
        Assert.Equal(keys.Select(k => Encoding.UTF8.GetBytes(k)), set.Definition.KeysUtf8);

        using var document = JsonDocument.Parse(SystemOneProtocol.QuestionsJson(set.Definition).ToArray());
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

            Assert.IsType(optionSets[i]!, plan.Options);
            Assert.Equal(wireKeys.Length, Count(plan.Options));
            Assert.Equal(Utf8Keys.Encode(wireKeys), set.Definition.OptionKeysUtf8[i]);
        }
    }

    private static int Count(object? options) => options switch
    {
        EnumOptionSet<Department> department => department.Count,
        EnumOptionSet<Frustration> frustration => frustration.Count,
        KeyedOptionSet keyed => keyed.Count,
        _ => throw new InvalidOperationException("The plan holds an option set this test does not expect."),
    };

    private static string[] WireKeys(JsonElement criteria)
        => criteria.ValueKind == JsonValueKind.Array
            ? Enumerable.Range(0, criteria.GetArrayLength()).Select(n => n.ToString(CultureInfo.InvariantCulture)).ToArray()
            : criteria.EnumerateObject().Select(p => p.Name).ToArray();
}
