using System.Text;
using Minos.Evaluation;

namespace Minos.AotSmoke;

/// <summary>The labelled dataset loader under Native AOT: text, a stream and a file, the expected answers, and the problems.</summary>
internal static class DatasetChecks
{
    private const string Line =
        """{"id":"a","state":"Where is my card?","questions":{"topic":{"type":"choice","instructions":"Pick the topic.","criteria":{"card":null,"fee":"A fee."}},"urgent":{"type":"noul","instructions":"Is it urgent?"}},"expected":{"topic":"card","urgent":false}}""";

    private const string BrokenLines = "{\"id\":\"a\"\n{\"id\":\"b\",\"extra\":1}\n";

    [Covers("static Minos.Evaluation.DecisionDataset.Parse(string! jsonl) -> ZeroAlloc.Results.Result<Minos.Evaluation.DecisionDataset!, Minos.Evaluation.DatasetProblems!>")]
    [Covers("Minos.Evaluation.DecisionDataset.GetEnumerator() -> System.Collections.Generic.IEnumerator<Minos.Evaluation.DatasetItem!>!")]
    public static void ParsesAString()
    {
        var parsed = DecisionDataset.Parse(Line);
        var items = new List<DatasetItem>();
        if (parsed.IsSuccess)
        {
            foreach (var item in parsed.Value)
            {
                items.Add(item);
            }
        }

        Program.Check(
            parsed.IsSuccess
                && items.Count == 1
                && string.Equals(items[0].Id, "a", StringComparison.Ordinal)
                && items[0].Definition.Questions.Count == 2,
            "a dataset line parses from text under Native AOT");
    }

    [Covers("static Minos.Evaluation.DecisionDataset.Parse(System.IO.Stream! utf8Jsonl) -> ZeroAlloc.Results.Result<Minos.Evaluation.DecisionDataset!, Minos.Evaluation.DatasetProblems!>")]
    public static void ParsesAStream()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Line + "\n"));

        var parsed = DecisionDataset.Parse(stream);

        Program.Check(
            parsed.IsSuccess && parsed.Value.Count == 1 && parsed.Value[0].Expected.Count == 2,
            "a dataset line parses from a stream under Native AOT");
    }

    [Covers("static Minos.Evaluation.DecisionDataset.Load(string! path) -> ZeroAlloc.Results.Result<Minos.Evaluation.DecisionDataset!, Minos.Evaluation.DatasetProblems!>")]
    public static void LoadsAFile()
    {
        var path = Path.Combine(Path.GetTempPath(), "minos-aot-smoke-" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllText(path, Line + "\n" + Line.Replace("\"id\":\"a\"", "\"id\":\"b\"", StringComparison.Ordinal) + "\n");
        try
        {
            var loaded = DecisionDataset.Load(path);

            Program.Check(
                loaded.IsSuccess && loaded.Value.Count == 2 && string.Equals(loaded.Value[1].Id, "b", StringComparison.Ordinal) && loaded.Value[1].Line == 2,
                "a dataset file loads under Native AOT");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Covers("static Minos.Evaluation.ExpectedAnswer.ForNoul(bool value) -> Minos.Evaluation.ExpectedAnswer")]
    [Covers("static Minos.Evaluation.ExpectedAnswer.ForChoice(string! key) -> Minos.Evaluation.ExpectedAnswer")]
    [Covers("static Minos.Evaluation.ExpectedAnswer.ForScore(int level) -> Minos.Evaluation.ExpectedAnswer")]
    [Covers("Minos.Evaluation.ExpectedAnswer.Equals(Minos.Evaluation.ExpectedAnswer other) -> bool")]
    [Covers("Minos.Evaluation.ExpectedAnswer.ExpectedAnswer() -> void")]
    public static void ExpectedAnswersCompareByValue()
    {
        var noul = ExpectedAnswer.ForNoul(true);
        var choice = ExpectedAnswer.ForChoice("card");
        var score = ExpectedAnswer.ForScore(2);
        var parsed = DecisionDataset.Parse(Line);
        var expected = parsed.IsSuccess ? parsed.Value[0].Expected : null;

        Program.Check(
            noul.Noul
                && noul.Kind == QuestionKind.Noul
                && string.Equals(choice.ChoiceKey, "card", StringComparison.Ordinal)
                && score.ScoreLevel == 2
                && noul.Equals(ExpectedAnswer.ForNoul(true))
                && !noul.Equals(ExpectedAnswer.ForNoul(false))
                && !choice.Equals(score)
                && default(ExpectedAnswer).Equals(new ExpectedAnswer())
                && expected is not null
                && expected["topic"].Equals(ExpectedAnswer.ForChoice("card"))
                && expected["urgent"].Equals(ExpectedAnswer.ForNoul(false)),
            "expected answers are built by kind and compared by value under Native AOT");
    }

    [Covers("Minos.Evaluation.DatasetProblem.DatasetProblem(int Line, string! Message) -> void")]
    [Covers("Minos.Evaluation.DatasetProblems.GetEnumerator() -> System.Collections.Generic.IEnumerator<Minos.Evaluation.DatasetProblem!>!")]
    public static void ProblemsCarryTheirLine()
    {
        var parsed = DecisionDataset.Parse(BrokenLines);
        var problems = new List<DatasetProblem>();
        if (parsed.IsFailure)
        {
            foreach (var problem in parsed.Error)
            {
                problems.Add(problem);
            }
        }

        var built = new DatasetProblem(2, "a message");

        Program.Check(
            parsed.IsFailure
                && problems.Count >= 2
                && problems[0].Line == 1
                && problems.Exists(problem => problem.Line == 2 && problem.Message.Length > 0)
                && built.Line == 2
                && string.Equals(built.Message, "a message", StringComparison.Ordinal),
            "a bad dataset line is reported with its line number under Native AOT");
    }
}
