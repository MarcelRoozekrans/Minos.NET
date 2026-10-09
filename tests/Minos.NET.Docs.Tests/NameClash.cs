namespace Shop.Support;

#region ClientAndErrors_NameClash
using Minos;
using Shop.Surveys;

// Shop.Surveys and Minos both have a Question, so a bare Question in this file would be ambiguous: error CS0104. An
// alias outranks a using of a whole namespace, so these two settle it. Question is your own, MinosQuestion is Minos's.
using Question = Shop.Surveys.Question;
using MinosQuestion = Minos.Question;

public static class SurveyRequests
{
    // Each survey question becomes a yes/no question in a raw request, under the survey question's own key.
    public static SystemOneRequest ToRequest(Survey survey, string state)
    {
        var questions = new Dictionary<string, MinosQuestion>(StringComparer.Ordinal);
        foreach (Question question in survey.Questions)
        {
            questions[question.Key] = new NoulQuestion { Instructions = question.Text };
        }

        return new SystemOneRequest { State = state, Questions = questions };
    }
}
#endregion
