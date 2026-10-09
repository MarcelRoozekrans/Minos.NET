using JevSharp.Abstractions.Models;
using JevSharp.Abstractions.Protocols;
using JevSharp.Abstractions.Requests;
using Microsoft.Extensions.Logging.Abstractions;
using JevSharpChoiceAnswer = JevSharp.Abstractions.Responses.ChoiceAnswer;
using JevSharpChoiceQuestion = JevSharp.Abstractions.Requests.ChoiceQuestion;
using JevSharpNoulAnswer = JevSharp.Abstractions.Responses.NoulAnswer;
using JevSharpNoulQuestion = JevSharp.Abstractions.Requests.NoulQuestion;
using JevSharpQuestion = JevSharp.Abstractions.Requests.JevQuestion;
using JevSharpClient = JevSharp.Core.Clients.JevClient;
using JevSharpOptions = JevSharp.Core.Configuration.JevClientOptions;

namespace Minos.Benchmarks.Compare.Adapters;

/// <summary>
/// JevSharp 0.2.0: a custom endpoint with the TypeSafe protocol, since JevSharp has no base-URL option, and
/// <c>Retry.MaxAttempts = 1</c>, over <see cref="BenchmarkTransport"/>'s <see cref="HttpClient"/>. A custom endpoint takes
/// static headers only; the empty map sends no auth header.
/// </summary>
public sealed class JevSharpAdapter : IClientAdapter, IUsesBenchmarkTransport
{
    private readonly HttpClient _http;
    private readonly JevSharpClient _client;
    private readonly JevRequest _request;

    /// <summary>Initializes a new instance of the <see cref="JevSharpAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>v1/systemone</c> under it.</param>
    public JevSharpAdapter(Uri baseAddress)
    {
        var options = new JevSharpOptions().UseCustom(
            ClientAdapters.SystemOneEndpoint(baseAddress),
            JevProtocol.TypeSafe,
            Workload.Model,
            new Dictionary<string, string>(StringComparer.Ordinal));
        options.Retry.MaxAttempts = 1;
        _http = BenchmarkTransport.CreateHttpClient();
        _client = new JevSharpClient(options, _http, NullLogger<JevSharpClient>.Instance);

        var criteria = new Dictionary<string, JevValue>(StringComparer.Ordinal);
        foreach (var (option, description) in Workload.IntentOptions)
        {
            criteria[option] = description;
        }

        _request = new JevRequest(
            Workload.State,
            new Dictionary<string, JevSharpQuestion>(StringComparer.Ordinal)
            {
                [Workload.IntentKey] = new JevSharpChoiceQuestion(Workload.IntentInstructions, criteria),
                [Workload.TravelsSoonKey] = new JevSharpNoulQuestion(Workload.TravelsSoonInstructions),
            });
    }

    /// <inheritdoc/>
    HttpClient IUsesBenchmarkTransport.Http => _http;

    /// <inheritdoc/>
    public string Client => ClientAdapters.JevSharp;

    /// <inheritdoc/>
    public string Library => "JevSharp";

    /// <inheritdoc/>
    public string Version => LibraryVersion.Of(typeof(JevSharpClient));

    /// <inheritdoc/>
    public string? Note => "JevSharp reaches a custom endpoint with static headers only, so it sends no auth header.";

    /// <inheritdoc/>
    public async ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
    {
        var response = await _client.EvaluateAsync(_request, cancellationToken).ConfigureAwait(false);
        var intent = response.GetAnswer<JevSharpChoiceAnswer>(Workload.IntentKey);
        var travelsSoon = response.GetAnswer<JevSharpNoulAnswer>(Workload.TravelsSoonKey);
        return new CallOutcome(string.Equals(intent.Choice, Workload.LookUpBooking, StringComparison.Ordinal), travelsSoon.Probability);
    }

    /// <inheritdoc/>
    public async Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken)
    {
        var response = await _client.EvaluateAsync(_request, cancellationToken).ConfigureAwait(false);
        var intent = response.GetAnswer<JevSharpChoiceAnswer>(Workload.IntentKey);
        var travelsSoon = response.GetAnswer<JevSharpNoulAnswer>(Workload.TravelsSoonKey);
        return new WorkloadAnswers(
            response.Model,
            intent.Choice,
            intent.Confidence ?? double.NaN,
            intent.Probabilities ?? new Dictionary<string, double>(StringComparer.Ordinal),
            travelsSoon.Probability);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _client.Dispose();
        _http.Dispose();
    }
}
