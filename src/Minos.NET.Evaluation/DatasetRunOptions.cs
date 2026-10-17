namespace Minos.Evaluation;

/// <summary>Settings for <see cref="DatasetRunner.RunAsync"/>.</summary>
public sealed class DatasetRunOptions
{
    /// <summary>Gets the most requests in flight at once. The default is 1; it must be at least 1.</summary>
    public int MaxParallelism { get; init; } = 1;

    /// <summary>Gets the name saved in the results header. The default is <c>dataset</c>; it must not be empty.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value is empty.</exception>
    public string DatasetName
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            field = value;
        }
    } = "dataset";

    /// <summary>Gets a receiver of the number of items completed so far, reported after each one.</summary>
    public IProgress<int>? Progress { get; init; }

    /// <summary>Gets the clock for the run's timestamps and each request's latency. The default is <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
}
