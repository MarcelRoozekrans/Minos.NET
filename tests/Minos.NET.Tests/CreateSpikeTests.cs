namespace Minos.Tests;

internal interface ICreateSpike<TSelf>
    where TSelf : ICreateSpike<TSelf>
{
    static abstract TSelf Create(AnswerSlots answers);
}

internal sealed class SpikeSet : ICreateSpike<SpikeSet>
{
    public double Probability { get; private init; }

    public static SpikeSet Create(AnswerSlots answers) => new() { Probability = answers.Noul(0).Probability };
}

public sealed class CreateSpikeTests
{
    private static T Build<T>(AnswerSlots answers)
        where T : ICreateSpike<T>
        => T.Create(answers);

    [Fact]
    public void StaticAbstractCreate_TakesAnswerSlots()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("n", "N?"));
        var answers = new AnswerSlots([new AnswerSlot(0, 0.4, 0, 0)], [], definition);

        Assert.Equal(0.4, Build<SpikeSet>(answers).Probability);
    }

    [Fact]
    public void AnswerFactory_TakesAnswerSlots()
    {
        AnswerFactory<double> factory = static answers => answers.Noul(0).Probability;
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("n", "N?"));

        Assert.Equal(0.4, factory(new AnswerSlots([new AnswerSlot(0, 0.4, 0, 0)], [], definition)));
    }
}
