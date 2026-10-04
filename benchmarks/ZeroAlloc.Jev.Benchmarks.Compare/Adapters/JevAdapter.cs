namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>ZeroAlloc.Jev: the generated <see cref="TravelRequest"/> question set through <c>EvaluateAsync&lt;T&gt;</c>.</summary>
public sealed class JevAdapter : IClientAdapter
{
    // The wire key of each TravelIntent option, by enum value, for reading every probability in AskAsync.
    private static readonly string[] OptionKeys = ["look_up_booking", "change_booking", "dispute_charge", "other"];

    private readonly JevClient _client;

    /// <summary>Initializes a new instance of the <see cref="JevAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>v1/systemone</c> under it.</param>
    public JevAdapter(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _client = new JevClient(new JevClientOptions
        {
            ApiKey = Workload.DummyApiKey,
            BaseAddress = ClientAdapters.WithTrailingSlash(baseAddress),
            MaxRetries = 0,
        });
    }

    /// <inheritdoc/>
    public string Client => "zeroalloc-jev";

    /// <inheritdoc/>
    public string Library => "ZeroAlloc.Jev";

    /// <inheritdoc/>
    public string Version => LibraryVersion.Of(typeof(JevClient));

    /// <inheritdoc/>
    public string? Note => null;

    /// <inheritdoc/>
    public async ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
    {
        var answers = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        return new CallOutcome(answers.Intent.Value == TravelIntent.LookUpBooking, answers.TravelsWithin24Hours.Probability);
    }

    /// <inheritdoc/>
    public async Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken)
    {
        var answers = await EvaluateAsync(cancellationToken).ConfigureAwait(false);
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (option, probability) in answers.Intent.Probabilities)
        {
            probabilities[OptionKeys[(int)option]] = probability;
        }

        // The typed API returns the answers only, so the model is not checked for this client.
        return new WorkloadAnswers(
            Model: null,
            OptionKeys[(int)answers.Intent.Value],
            answers.Intent.Confidence,
            probabilities,
            answers.TravelsWithin24Hours.Probability);
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();

    private async ValueTask<TravelRequest> EvaluateAsync(CancellationToken cancellationToken)
    {
        var result = await _client.EvaluateAsync<TravelRequest>(Workload.State, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("ZeroAlloc.Jev call failed: " + result.Error.Kind + ": " + result.Error.Message);
    }
}
