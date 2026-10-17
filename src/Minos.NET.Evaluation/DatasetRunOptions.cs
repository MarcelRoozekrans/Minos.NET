namespace Minos.Evaluation;

/// <summary>Settings for <see cref="DatasetRunner.RunAsync"/>.</summary>
public sealed class DatasetRunOptions
{
    /// <summary>Gets the most requests in flight at once. The default is 1; it must be at least 1.</summary>
    public int MaxParallelism { get; init; } = 1;

    /// <summary>Gets the name saved in the results header. The default is <c>dataset</c>; it must not be empty or hold an unpaired surrogate.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The value is empty or holds an unpaired surrogate.</exception>
    public string DatasetName
    {
        get;
        init
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            if (!ResultsRules.IsValidText(value))
            {
                throw new ArgumentException("The dataset name holds an unpaired surrogate.", nameof(value));
            }

            field = value;
        }
    } = "dataset";

    /// <summary>Gets a receiver of the number of items completed so far, reported after each one.</summary>
    public IProgress<int>? Progress { get; init; }

    /// <summary>Gets the clock for the run's timestamps and each request's latency. The default is <see cref="TimeProvider.System"/>.</summary>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public TimeProvider TimeProvider
    {
        get;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = TimeProvider.System;
}
