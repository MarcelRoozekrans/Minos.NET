namespace Minos.Tool.Tests;

/// <summary>Tests that change process environment variables run alone, so no other test class observes them.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Name = "Environment";
}
