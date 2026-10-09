using Shop.Support;
using Shop.Surveys;

namespace Minos.Docs.Tests;

public sealed class NameClashTests
{
    [Fact]
    public void ToRequest_TurnsEachSurveyQuestionIntoANoulQuestion()
    {
        var survey = new Survey
        {
            Questions =
            [
                new Shop.Surveys.Question { Key = "urgent", Text = "Does this convey urgency?" },
                new Shop.Surveys.Question { Key = "refund", Text = "Is a refund asked for?" },
            ],
        };

        var request = SurveyRequests.ToRequest(survey, "I was charged twice, fix it today.");

        Assert.Equal(["refund", "urgent"], request.Questions.Keys.Order(StringComparer.Ordinal));
        Assert.IsType<NoulQuestion>(request.Questions["urgent"]);
    }
}
