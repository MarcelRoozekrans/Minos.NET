namespace Minos.Samples.TicketTriage;

/// <summary>The counts of one triage run. The last three count questions, not items.</summary>
/// <param name="Items">The dataset items run.</param>
/// <param name="Decided">The items the local client answered.</param>
/// <param name="Failed">The items the local client returned an error for; their line has an error and no questions.</param>
/// <param name="EscalatedQuestions">The questions the target answered.</param>
/// <param name="NoTimeLeft">The unsure questions kept on the local answer because the budget had run out.</param>
/// <param name="TargetFailed">The unsure questions kept on the local answer because the target failed.</param>
public sealed record TriageSummary(int Items, int Decided, int Failed, int EscalatedQuestions, int NoTimeLeft, int TargetFailed);
