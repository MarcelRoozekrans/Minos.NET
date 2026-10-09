namespace Minos.Docs.Tests;

#region Diagnostics_Suppress
using Minos;

// The Jev API's guidance is 2 to 10 levels for a Score, so JEV005 warns about this enum. A 0 to 10 scale has 11
// levels by definition, so the warning is suppressed here, for this enum only, and the reason is written next to it.
#pragma warning disable JEV005 // A 0 to 10 recommendation scale has 11 levels on purpose.
public enum Recommendation
{
    [Level("0: Not at all likely")] Zero,
    [Level("1")] One,
    [Level("2")] Two,
    [Level("3")] Three,
    [Level("4")] Four,
    [Level("5: Neutral")] Five,
    [Level("6")] Six,
    [Level("7")] Seven,
    [Level("8")] Eight,
    [Level("9")] Nine,
    [Level("10: Extremely likely")] Ten,
}
#pragma warning restore JEV005

[JevQuestions]
public partial record SurveyReply
{
    [Score("How likely is this customer to recommend us to a friend?")]
    public partial Score<Recommendation> Recommend { get; }
}
#endregion
