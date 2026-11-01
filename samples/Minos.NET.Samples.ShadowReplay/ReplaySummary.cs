namespace Minos.Samples.ShadowReplay;

/// <summary>The counts of one replay.</summary>
/// <param name="Items">The dataset items replayed.</param>
/// <param name="Decided">The items the primary answered.</param>
/// <param name="PrimaryFailed">The items the primary returned an error for; they have no shadow.</param>
/// <param name="Shadowed">The items whose record was written with the candidate's part, answers or error.</param>
/// <param name="WaitsRunOut">The decided items whose shadow write did not come within the wait.</param>
public sealed record ReplaySummary(int Items, int Decided, int PrimaryFailed, int Shadowed, int WaitsRunOut);
