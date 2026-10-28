using System.Globalization;
using System.Text;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI;

/// <summary>Builds the chat messages that ask one question about a request's state.</summary>
internal static class QuestionPrompt
{
    // The answer is the first output token, so the model is told to give the label alone.
    private const string LabelSystem =
        "You are a classifier. You answer one question about the state the user gives you. "
        + "Answer with one label: the capital letter of the option that fits best, and nothing else.";

    private const string VerbalizedSystem =
        "You are a classifier. You answer one question about the state the user gives you. "
        + "Give the probability of each label: a number from 0 to 1 for every option, as a JSON object keyed by label.";

    /// <summary>
    /// The system message, then one user message: the question, its labelled options or levels with their criteria, the
    /// state, and any images as data parts.
    /// </summary>
    internal static IList<ChatMessage> Build(QuestionDefinition question, DecisionRequest request, bool verbalized)
    {
        var text = new StringBuilder();
        text.Append("Question: ").Append(question.Instructions.ToString()).Append("\n\nOptions:\n");

        int labelCount;
        if (question.Kind == QuestionKind.Noul)
        {
            labelCount = 2;
            AppendNoul(text, 0, "Yes", question.WhenTrue);
            AppendNoul(text, 1, "No", question.WhenFalse);
        }
        else
        {
            var options = question.Options;
            labelCount = options.Count;
            for (var i = 0; i < options.Count; i++)
            {
                var name = question.Kind == QuestionKind.Score ? "Level " + i.ToString(CultureInfo.InvariantCulture) : options[i].Key;
                AppendOption(text, i, name, options[i].Criterion);
            }
        }

        text.Append("\nState:\n").Append(request.State.ToString()).Append("\n\n");
        text.Append(verbalized ? "Give the probability of each label, " : "Answer with one label, ").Append(Range(labelCount))
            .Append(verbalized ? ", as a JSON object keyed by label." : ".");

        var user = new ChatMessage(ChatRole.User, [new TextContent(text.ToString())]);
        foreach (var image in request.Images)
        {
            user.Contents.Add(new DataContent(image.Data, image.MediaType));
        }

        return [new ChatMessage(ChatRole.System, verbalized ? VerbalizedSystem : LabelSystem), user];
    }

    private static void AppendNoul(StringBuilder text, int index, string name, DecisionContent? meaning)
    {
        text.Append(LabelDistribution.Label(index)).Append(". ").Append(name);
        if (meaning is { } value)
        {
            text.Append(": ").Append(value.ToString());
        }

        text.Append('\n');
    }

    private static void AppendOption(StringBuilder text, int index, string name, Criterion? criterion)
    {
        text.Append(LabelDistribution.Label(index)).Append(". ").Append(name);
        if (criterion is null)
        {
            text.Append('\n');
            return;
        }

        text.Append(": ").Append(criterion.Description ?? criterion.JsonContent?.ToString()).Append('\n');
        foreach (var example in criterion.Examples)
        {
            text.Append("   Example: ").Append(example).Append('\n');
        }

        foreach (var notFor in criterion.NotFor)
        {
            text.Append("   Not for: ").Append(notFor).Append('\n');
        }
    }

    // "A", "A or B", or "A to C".
    private static string Range(int labelCount) => labelCount switch
    {
        1 => "A",
        2 => "A or B",
        _ => "A to " + LabelDistribution.Label(labelCount - 1),
    };
}
