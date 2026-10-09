namespace Minos.DependencyInjection.Tests;

/// <summary>
/// The collection every test class joins when it sets a process environment variable. Environment variables are
/// process-wide, so this collection runs alone, after every parallel one.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Process environment";
}

/// <summary>Sets environment variables for one test, and restores their earlier values when disposed.</summary>
internal sealed class EnvironmentVariables : IDisposable
{
    private readonly (string Name, string? Value)[] _saved;

    /// <param name="variables">Each variable and its value; a <see langword="null"/> value removes the variable.</param>
    public EnvironmentVariables(params (string Name, string? Value)[] variables)
    {
        _saved = [.. variables.Select(variable => (variable.Name, Environment.GetEnvironmentVariable(variable.Name)))];
        foreach (var (name, value) in variables)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }

    public void Dispose()
    {
        foreach (var (name, value) in _saved)
        {
            Environment.SetEnvironmentVariable(name, value);
        }
    }
}
