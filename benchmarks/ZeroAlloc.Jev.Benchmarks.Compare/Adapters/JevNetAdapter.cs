using Jev.Net;
using JevNetChoice = Jev.Net.Choice;
using JevNetClient = Jev.Net.TypeSafeClient;
using JevNetNoul = Jev.Net.Noul;
using JevNetOptions = Jev.Net.TypeSafeClientOptions;
using JevNetRetryPolicy = Jev.Net.RetryPolicy;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// Jev.Net 0.4.0: <c>BaseUrl</c> pointed at the mock, <c>RetryPolicy.None</c> and the dummy key, over
/// <see cref="BenchmarkTransport"/>'s <see cref="HttpClient"/>.
/// </summary>
public sealed class JevNetAdapter : IClientAdapter
{
    private readonly HttpClient _http;
    private readonly JevNetClient _client;
    private readonly Dictionary<string, Question> _questions;

    /// <summary>Initializes a new instance of the <see cref="JevNetAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>/v1/systemone</c> under it.</param>
    public JevNetAdapter(Uri baseAddress)
    {
        _http = BenchmarkTransport.CreateHttpClient();
        _client = new JevNetClient(new JevNetOptions
        {
            HttpClient = _http,
            DisposeHttpClient = false,
            ApiKey = Workload.DummyApiKey,
            BaseUrl = ClientAdapters.WithoutTrailingSlash(baseAddress),
            Retry = JevNetRetryPolicy.None,
        });

        var criteria = new Dictionary<string, JsonContent?>(StringComparer.Ordinal);
        foreach (var (option, description) in Workload.IntentOptions)
        {
            criteria[option] = description;
        }

        _questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            [Workload.IntentKey] = new JevNetChoice(criteria, Workload.IntentInstructions),
            [Workload.TravelsSoonKey] = new JevNetNoul(Workload.TravelsSoonInstructions),
        };
    }

    /// <inheritdoc/>
    public string Client => ClientAdapters.JevNet;

    /// <inheritdoc/>
    public string Library => "Jev.Net";

    /// <inheritdoc/>
    public string Version => LibraryVersion.Of(typeof(JevNetClient));

    /// <inheritdoc/>
    public string? Note => null;

    /// <inheritdoc/>
    public async ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
    {
        var result = await _client.SystemOneAsync(Workload.State, _questions, options: null, cancellationToken).ConfigureAwait(false);
        var intent = result.Choices[Workload.IntentKey];
        return new CallOutcome(string.Equals(intent.Choice, Workload.LookUpBooking, StringComparison.Ordinal), result.Nouls[Workload.TravelsSoonKey].Noul);
    }

    /// <inheritdoc/>
    public async Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken)
    {
        var result = await _client.SystemOneAsync(Workload.State, _questions, options: null, cancellationToken).ConfigureAwait(false);
        var intent = result.Choices[Workload.IntentKey];
        return new WorkloadAnswers(result.Model, intent.Choice, intent.Confidence, intent.Probabilities, result.Nouls[Workload.TravelsSoonKey].Noul);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
    }
}
