namespace Minos.Docs.Tests;

/// <summary>
/// A fact that runs only when <c>MINOS_UPSTREAM_ABSTRACTIONS</c> names a built Microsoft.Extensions.AI.Abstractions
/// assembly, such as the one <c>tools/upstream/build-decisions.sh</c> writes the path of. The types are not on NuGet,
/// so without the variable the fact is skipped, with that reason shown in the test run.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class UpstreamAbstractionsFactAttribute : FactAttribute
{
    /// <summary>The variable holding the path of the built upstream assembly.</summary>
    public const string Variable = "MINOS_UPSTREAM_ABSTRACTIONS";

    public UpstreamAbstractionsFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = "Set " + Variable + " to the built Microsoft.Extensions.AI.Abstractions.dll to run this test; "
                + "run tools/upstream/build-decisions.sh to build the pinned upstream and write its path to artifacts/upstream/abstractions.path.";
        }
    }
}
