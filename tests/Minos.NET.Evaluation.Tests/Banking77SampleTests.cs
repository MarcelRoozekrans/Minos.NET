namespace Minos.Evaluation.Tests;

// The Banking77 sample feeds the calibration reports of Phase 8.4, so its shape is pinned here: counts, rates and ids.
public sealed class Banking77SampleTests
{
    private static readonly string[] Intents =
    [
        "card_arrival", "card_delivery_estimate", "card_linking", "card_not_working", "lost_or_stolen_card",
        "declined_card_payment", "pending_card_payment", "card_payment_fee_charged", "activate_my_card", "change_pin",
    ];

    private static DecisionDataset Load(string name)
    {
        var result = DecisionDataset.Load(Path.Combine(AppContext.BaseDirectory, "Banking77", name));
        Assert.True(result.IsSuccess, result.IsSuccess ? null : string.Join("; ", result.Error.Select(p => $"{p.Line}: {p.Message}")));
        return result.Value;
    }

    [Fact]
    public void TheMainFile_Loads_With400Items_And40PerIntent()
    {
        var dataset = Load("banking77-minos-v1.jsonl");

        Assert.Equal(400, dataset.Count);
        var perIntent = dataset.GroupBy(i => i.Expected["intent"].ChoiceKey!).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(Intents.Order(), perIntent.Keys.Order());
        Assert.All(perIntent.Values, count => Assert.Equal(40, count));
    }

    [Fact]
    public void TheMainFile_HasTheThreeQuestions_AndTheNoulsFollowTheIntent()
    {
        var dataset = Load("banking77-minos-v1.jsonl");
        string[] arrival = ["card_arrival", "card_delivery_estimate"];
        string[] payment = ["declined_card_payment", "pending_card_payment", "card_payment_fee_charged"];

        string[] keys = ["intent", "card_not_arrived", "about_a_payment"];
        foreach (var item in dataset)
        {
            var questions = item.Definition.Questions;
            Assert.Equal(3, questions.Count);
            Assert.Equal(keys[0], questions[0].Key);
            Assert.Equal(keys[1], questions[1].Key);
            Assert.Equal(keys[2], questions[2].Key);
            var intent = item.Expected["intent"].ChoiceKey!;
            Assert.Equal(arrival.Contains(intent), item.Expected["card_not_arrived"].Noul);
            Assert.Equal(payment.Contains(intent), item.Expected["about_a_payment"].Noul);
        }

        var first = dataset[0].Definition.Questions;
        Assert.Equal(Intents, first[0].Options.Select(o => o.Key));
        Assert.Equal(QuestionKind.Noul, first[1].Kind);
        Assert.Equal(QuestionKind.Noul, first[2].Kind);

        Assert.Equal(0.20, dataset.Count(i => i.Expected["card_not_arrived"].Noul) / (double)dataset.Count, 6);
        Assert.Equal(0.30, dataset.Count(i => i.Expected["about_a_payment"].Noul) / (double)dataset.Count, 6);
    }

    [Fact]
    public void TheMainFile_HasUniqueIds_ThatNameTheIntent()
    {
        var dataset = Load("banking77-minos-v1.jsonl");

        Assert.Equal(400, dataset.Select(i => i.Id).Distinct().Count());
        Assert.All(dataset, i => Assert.StartsWith($"b77-{i.Expected["intent"].ChoiceKey}-", i.Id, StringComparison.Ordinal));
        Assert.Equal(400, dataset.Select(i => i.Source).Distinct().Count());
        Assert.All(dataset, i => AssertUpstreamRow(i.Source));
    }

    private static void AssertUpstreamRow(string? source)
    {
        Assert.NotNull(source);
        Assert.StartsWith("banking77/test#", source, StringComparison.Ordinal);
        Assert.True(int.TryParse(source.AsSpan("banking77/test#".Length), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var row), source);
        Assert.InRange(row, 1, 3080);
    }

    [Fact]
    public void TheUrgencyFile_Loads_With60Items_AndLevels0To2()
    {
        var dataset = Load("banking77-urgency-v1.jsonl");

        Assert.Equal(60, dataset.Count);
        Assert.Equal(60, dataset.Select(i => i.Id).Distinct().Count());
        Assert.Equal(60, dataset.Select(i => i.Source).Distinct().Count());
        Assert.All(dataset, i => AssertUpstreamRow(i.Source));
        Assert.All(dataset, i => Assert.Equal(["urgency"], i.Definition.Questions.Select(q => q.Key)));
        Assert.All(dataset, i => Assert.InRange(i.Expected["urgency"].ScoreLevel, 0, 2));
        Assert.Equal([0, 1, 2], dataset[0].Definition.Questions[0].Options.Select((_, n) => n));
    }
}
