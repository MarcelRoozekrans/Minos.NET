using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZeroAlloc.Jev.Benchmarks.Compare.Adapters;

/// <summary>
/// The baseline: what a careful developer writes by hand with <see cref="HttpClient"/> and System.Text.Json. It
/// serializes the request body ZeroAlloc.Jev sends for the workload once, posts those bytes with a <c>Content-Length</c>
/// as every library does, and reads the answers into plain records, both through a source-generated
/// <see cref="JsonSerializerContext"/>, over <see cref="BenchmarkTransport"/>'s <see cref="HttpClient"/>. One attempt,
/// no retries.
/// </summary>
public sealed class RawHttpAdapter : IClientAdapter, IUsesBenchmarkTransport
{
    private static readonly Uri SystemOnePath = new("v1/systemone", UriKind.Relative);
    private static readonly MediaTypeHeaderValue Json = new("application/json");

    private readonly HttpClient _http;
    private readonly byte[] _body;

    /// <summary>Initializes a new instance of the <see cref="RawHttpAdapter"/> class.</summary>
    /// <param name="baseAddress">The mock's root address; requests go to <c>v1/systemone</c> under it.</param>
    public RawHttpAdapter(Uri baseAddress)
    {
        _http = BenchmarkTransport.CreateHttpClient(ClientAdapters.WithTrailingSlash(baseAddress));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Workload.DummyApiKey);

        var criteria = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (option, description) in Workload.IntentOptions)
        {
            criteria[option] = description;
        }

        var request = new RawRequest(
            Workload.State,
            Workload.Model,
            new RawQuestions(
                new RawChoiceQuestion("choice", Workload.IntentInstructions, criteria),
                new RawNoulQuestion("noul", Workload.TravelsSoonInstructions)));
        _body = JsonSerializer.SerializeToUtf8Bytes(request, RawJsonContext.Default.RawRequest);
    }

    /// <inheritdoc/>
    HttpClient IUsesBenchmarkTransport.Http => _http;

    /// <inheritdoc/>
    public string Client => ClientAdapters.Raw;

    /// <inheritdoc/>
    public string Library => "HttpClient, System.Text.Json";

    /// <inheritdoc/>
    public string Version => Environment.Version.ToString();

    /// <inheritdoc/>
    public string? Note => null;

    /// <inheritdoc/>
    public async ValueTask<CallOutcome> CallAsync(CancellationToken cancellationToken)
    {
        var response = await PostAsync(cancellationToken).ConfigureAwait(false);
        var answers = response.Answers;
        return new CallOutcome(
            string.Equals(answers.Intent.Choice, Workload.LookUpBooking, StringComparison.Ordinal),
            answers.TravelsWithin24Hours.Noul);
    }

    /// <inheritdoc/>
    public async Task<WorkloadAnswers> AskAsync(CancellationToken cancellationToken)
    {
        var response = await PostAsync(cancellationToken).ConfigureAwait(false);
        var intent = response.Answers.Intent;
        return new WorkloadAnswers(response.Model, intent.Choice, intent.Confidence, intent.Probabilities, response.Answers.TravelsWithin24Hours.Noul);
    }

    /// <inheritdoc/>
    public void Dispose() => _http.Dispose();

    private async Task<RawResponse> PostAsync(CancellationToken cancellationToken)
    {
        // A ByteArrayContent knows its length, so the request carries a Content-Length instead of a chunked body.
        using var content = new ByteArrayContent(_body);
        content.Headers.ContentType = Json;
        using var response = await _http.PostAsync(SystemOnePath, content, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync(RawJsonContext.Default.RawResponse, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The response body was null.");
    }
}

/// <summary>The <c>/v1/systemone</c> request body.</summary>
/// <param name="State">The text the questions are about.</param>
/// <param name="Model">The model to ask.</param>
/// <param name="Questions">The questions, by key.</param>
public sealed record RawRequest(string State, string Model, RawQuestions Questions);

/// <summary>The workload's two questions, under their wire keys.</summary>
/// <param name="Intent">The <c>intent</c> Choice question.</param>
/// <param name="TravelsWithin24Hours">The <c>travels_within24_hours</c> Noul question.</param>
public sealed record RawQuestions(
    [property: JsonPropertyName("intent")] RawChoiceQuestion Intent,
    [property: JsonPropertyName("travels_within24_hours")] RawNoulQuestion TravelsWithin24Hours);

/// <summary>A Choice question.</summary>
/// <param name="Type">Always <c>choice</c>.</param>
/// <param name="Instructions">What to decide.</param>
/// <param name="Criteria">Each option's description, in order.</param>
public sealed record RawChoiceQuestion(string Type, string Instructions, IReadOnlyDictionary<string, string> Criteria);

/// <summary>A Noul question.</summary>
/// <param name="Type">Always <c>noul</c>.</param>
/// <param name="Instructions">The yes-or-no question.</param>
public sealed record RawNoulQuestion(string Type, string Instructions);

/// <summary>The parts of a <c>/v1/systemone</c> response the baseline reads.</summary>
/// <param name="Model">The model that answered.</param>
/// <param name="Answers">The answers, by key.</param>
public sealed record RawResponse(string Model, RawAnswers Answers);

/// <summary>The workload's two answers, under their wire keys.</summary>
/// <param name="Intent">The <c>intent</c> answer.</param>
/// <param name="TravelsWithin24Hours">The <c>travels_within24_hours</c> answer.</param>
public sealed record RawAnswers(
    [property: JsonPropertyName("intent")] RawChoiceAnswer Intent,
    [property: JsonPropertyName("travels_within24_hours")] RawNoulAnswer TravelsWithin24Hours);

/// <summary>A Choice answer.</summary>
/// <param name="Choice">The chosen option.</param>
/// <param name="Probabilities">Each option's probability.</param>
/// <param name="Confidence">How sure the model is.</param>
public sealed record RawChoiceAnswer(string Choice, IReadOnlyDictionary<string, double> Probabilities, double Confidence);

/// <summary>A Noul answer.</summary>
/// <param name="Noul">The probability of yes.</param>
public sealed record RawNoulAnswer(double Noul);

/// <summary>The baseline's source-generated serializer metadata, with the API's snake_case names.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RawRequest))]
[JsonSerializable(typeof(RawResponse))]
internal sealed partial class RawJsonContext : JsonSerializerContext;
