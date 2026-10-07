using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ZeroAlloc.Jev.AotSurface;

/// <summary>
/// Instantiates every public generic type and generic method of both packages over concrete types. Rooting an assembly
/// makes ILC analyse a generic member only through an instantiation it can share across reference types, and a
/// value-type generic, such as <see cref="Choice{T}"/> with its <c>where T : struct, Enum</c>, has none, so rooting the
/// packages alone skips it. The csproj roots this assembly, so ILC analyses these methods and, through them, every
/// instantiation below. Nothing calls them; publishing is the check.
/// </summary>
/// <remarks>
/// A generic type is instantiated with a <see cref="DynamicDependencyAttribute"/> for all its members, and a generic
/// method by a call. The test <c>SurfaceGenericsTests</c> in <c>tests/ZeroAlloc.Jev.AotSmoke.Tests</c> fails when a
/// public generic type or method of either package is missing here.
/// </remarks>
public static class SurfaceGenerics
{
    /// <summary>Instantiates every public generic type, with all its members.</summary>
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Choice<SurfaceTeam>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChoiceHandle<SurfaceTeam>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ChoiceOptionsBuilder<SurfaceTeam>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(JevOptionSet<SurfaceTeam>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ProbabilityMap<SurfaceTeam>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ProbabilityMap<SurfaceTeam>.Enumerator))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(Score<SurfaceUrgency>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ScoreHandle<SurfaceUrgency>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ScoreLevelsBuilder<SurfaceUrgency>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IJevQuestionSet<SurfaceTriage>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(IJevQuestionSet<SurfaceStateTriage, SurfaceState>))]
    public static void Types()
    {
    }

    /// <summary>Calls every public generic method that does not evaluate.</summary>
    /// <remarks>The builder callbacks are empty, so a generic type's members are reached only through
    /// <see cref="Types"/>.</remarks>
    public static void Methods(JevAnswers answers, JevOptionSet<SurfaceTeam> teams, JevOptionSet<SurfaceUrgency> levels, SurfaceState state)
    {
        var reader = new Utf8JsonReader([]);
        _ = JevAnswerReader.ReadChoice(ref reader, teams, [], 0);
        _ = JevAnswerReader.ReadScore(ref reader, levels, [], 0);

        _ = JevQuestionSet.CreateBuilder()
            .Choice<SurfaceTeam>("team", "Which team?", out var team)
            .Choice<SurfaceTeam>("described_team", "Which team?", out _, static _ => { })
            .Score<SurfaceUrgency>("urgency", "How urgent?", out var urgency, static _ => { });
        _ = answers.Get(team);
        _ = answers.Get(urgency);

        _ = JevContent.FromValue(state, SurfaceStateJsonContext.Default.SurfaceState);
    }

    /// <summary>Calls every public generic evaluation method, on the interface and on the client.</summary>
    public static async Task EvaluateAsync(IJevClient client, JevClient jevClient, JsonElement json, SurfaceState state)
    {
        var stateInfo = SurfaceStateJsonContext.Default.SurfaceState;

        _ = await client.EvaluateAsync<SurfaceTriage>("state").ConfigureAwait(false);
        _ = await client.EvaluateAsync<SurfaceTriage>("state", CancellationToken.None).ConfigureAwait(false);
        _ = await client.EvaluateAsync<SurfaceTriage>(json).ConfigureAwait(false);
        _ = await client.EvaluateAsync<SurfaceTriage>(json, CancellationToken.None).ConfigureAwait(false);
        _ = await client.EvaluateUtf8Async<SurfaceTriage>("{}"u8.ToArray()).ConfigureAwait(false);
        _ = await client.EvaluateAsync<SurfaceStateTriage, SurfaceState>(state, stateInfo).ConfigureAwait(false);
        _ = await client.EvaluateAsync<SurfaceStateTriage, SurfaceState>(state, stateInfo, CancellationToken.None).ConfigureAwait(false);

        _ = await jevClient.EvaluateAsync<SurfaceTriage>("state").ConfigureAwait(false);
        _ = await jevClient.EvaluateAsync<SurfaceTriage>("state", CancellationToken.None).ConfigureAwait(false);
        _ = await jevClient.EvaluateAsync<SurfaceTriage>(json).ConfigureAwait(false);
        _ = await jevClient.EvaluateAsync<SurfaceTriage>(json, CancellationToken.None).ConfigureAwait(false);
        _ = await jevClient.EvaluateUtf8Async<SurfaceTriage>("{}"u8.ToArray()).ConfigureAwait(false);
        _ = await jevClient.EvaluateAsync<SurfaceStateTriage, SurfaceState>(state, stateInfo).ConfigureAwait(false);
        _ = await jevClient.EvaluateAsync<SurfaceStateTriage, SurfaceState>(state, stateInfo, CancellationToken.None).ConfigureAwait(false);
    }
}
