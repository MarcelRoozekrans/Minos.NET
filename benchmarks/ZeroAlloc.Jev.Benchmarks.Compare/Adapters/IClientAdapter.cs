namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// One benchmark client: a single long-lived client instance pointed at the mock, making one attempt per call and
/// asking <see cref="Workload"/>'s questions.
/// </summary>
public interface IClientAdapter : IDisposable
{
    /// <summary>Gets the client's name in the result file.</summary>
    string Client { get; }

    /// <summary>Gets the library the client is built on.</summary>
    string Library { get; }

    /// <summary>Gets the library's version.</summary>
    string Version { get; }

    /// <summary>Gets a note on how this client's calls differ from the others', or <see langword="null"/>.</summary>
    string? Note { get; }

    /// <summary>Makes one call and reads what a caller acts on. This is the measured call.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The chosen option and the Noul probability.</returns>
    ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken);

    /// <summary>Makes one call and reads every answer, for checking the client parsed the recorded response.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>Every answer.</returns>
    Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken);
}
