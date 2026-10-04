namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// ZeroAlloc.Jev: the generated <see cref="TravelRequest"/> question set through <c>EvaluateAsync&lt;T&gt;</c>, over
/// <see cref="BenchmarkTransport"/>'s <see cref="HttpClient"/>.
/// </summary>
public sealed class JevAdapter : IClientAdapter, IUsesBenchmarkTransport
{
    // The wire key of each TravelIntent option, by enum value, for reading every probability in AskAsync. TravelIntent
    // declares its options in the workload's order.
    private static readonly string[] OptionKeys = Workload.IntentOptions.Select(o => o.Key).ToArray();

    private readonly HttpClient _http;
    private readonly JevClient _client;

    /// <summary>Initializes a new instance of the <see cref="JevAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>v1/systemone</c> under it.</param>
    public JevAdapter(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        _http = BenchmarkTransport.CreateHttpClient();
        _client = new JevClient(_http, new JevClientOptions
        {
            ApiKey = Workload.DummyApiKey,
            BaseAddress = ClientAdapters.WithTrailingSlash(baseAddress),
            MaxRetries = 0,
        });
    }

    /// <inheritdoc/>
    HttpClient IUsesBenchmarkTransport.Http => _http;

    /// <inheritdoc/>
    public string Client => ClientAdapters.Jev;

    /// <inheritdoc/>
    public string Library => "ZeroAlloc.Jev";

    /// <inheritdoc/>
    public string Version => LibraryVersion.OfSourceBuild(typeof(JevClient));

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
    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
    }

    private async ValueTask<TravelRequest> EvaluateAsync(CancellationToken cancellationToken)
    {
        var result = await _client.EvaluateAsync<TravelRequest>(Workload.State, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("ZeroAlloc.Jev call failed: " + result.Error.Kind + ": " + result.Error.Message);
    }
}
