using TypeSafe.AI.Sdk;
using TypeSafeSdkClient = TypeSafe.AI.Sdk.TypeSafeClient;
using TypeSafeSdkOptions = TypeSafe.AI.Sdk.TypeSafeClientOptions;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>TypeSafe.AI.Sdk 0.3.0: <c>BaseUrl</c> pointed at the mock, <c>Retry.MaxRetries = 0</c> and the dummy key.</summary>
public sealed class TypeSafeSdkAdapter : IClientAdapter
{
    private readonly TypeSafeSdkClient _client;
    private readonly Dictionary<string, Question> _questions;

    /// <summary>Initializes a new instance of the <see cref="TypeSafeSdkAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>/v1/systemone</c> under it.</param>
    public TypeSafeSdkAdapter(Uri baseAddress)
    {
        _client = new TypeSafeSdkClient(new TypeSafeSdkOptions
        {
            ApiKey = Workload.DummyApiKey,
            BaseUrl = ClientAdapters.WithoutTrailingSlash(baseAddress),
            Retry = new RetryPolicy { MaxRetries = 0 },
        });

        var criteria = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (option, description) in Workload.IntentOptions)
        {
            criteria[option] = description;
        }

        _questions = new Dictionary<string, Question>(StringComparer.Ordinal)
        {
            [Workload.IntentKey] = Question.Choice(Workload.IntentInstructions, criteria),
            [Workload.TravelsSoonKey] = Question.Noul(Workload.TravelsSoonInstructions, criteria: null),
        };
    }

    /// <inheritdoc/>
    public string Client => ClientAdapters.TypeSafeSdk;

    /// <inheritdoc/>
    public string Library => "TypeSafe.AI.Sdk";

    /// <inheritdoc/>
    public string Version => LibraryVersion.Of(typeof(TypeSafeSdkClient));

    /// <inheritdoc/>
    public string? Note => null;

    /// <inheritdoc/>
    public async ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
    {
        var result = await _client.SystemOneAsync(Workload.State, _questions, Workload.Model, options: null, cancellationToken).ConfigureAwait(false);
        var intent = result.GetChoice(Workload.IntentKey);
        var travelsSoon = result.GetNoul(Workload.TravelsSoonKey);
        return new CallOutcome(string.Equals(intent.Choice, Workload.LookUpBooking, StringComparison.Ordinal), travelsSoon.Noul);
    }

    /// <inheritdoc/>
    public async Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken)
    {
        var result = await _client.SystemOneAsync(Workload.State, _questions, Workload.Model, options: null, cancellationToken).ConfigureAwait(false);
        var intent = result.GetChoice(Workload.IntentKey);
        var travelsSoon = result.GetNoul(Workload.TravelsSoonKey);
        return new WorkloadAnswers(result.Model, intent.Choice, intent.Confidence, intent.Probabilities, travelsSoon.Noul);
    }

    /// <inheritdoc/>
    public void Dispose() => _client.Dispose();
}
