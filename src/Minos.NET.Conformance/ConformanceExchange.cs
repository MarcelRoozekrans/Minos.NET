using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Minos.Conformance;

/// <summary>
/// One HTTP exchange of a probe: the request body, the response status, its media type, its allowlisted headers and its
/// body. In a fixture, an exchange may instead say why its probe is skipped.
/// </summary>
public sealed class ConformanceExchange
{
    /// <summary>Gets the probe's id.</summary>
    public required string Probe { get; init; }

    /// <summary>Gets the date this exchange was recorded, as <c>yyyy-MM-dd</c>, when it differs from the fixture's own; otherwise <see langword="null"/>.</summary>
    public string? Recorded { get; init; }

    /// <summary>Gets why the probe is skipped when the fixture has no exchange for it yet, or <see langword="null"/>.</summary>
    public string? Skipped { get; init; }

    /// <summary>Gets the request body, as JSON, or <see langword="null"/> when it was not kept.</summary>
    public JsonElement? Request { get; init; }

    /// <summary>Gets the response's HTTP status, or <see langword="null"/> for a skipped probe.</summary>
    public int? Status { get; init; }

    /// <summary>Gets the response's <c>Content-Type</c>, or <see langword="null"/> when it had none.</summary>
    public string? ContentType { get; init; }

    /// <summary>Gets the allowlisted response headers by lower-case name, or <see langword="null"/> when there were none.</summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Gets the response body: its JSON when <see cref="ContentType"/> is JSON and it parses, otherwise a JSON string holding its text.</summary>
    public JsonElement? Response { get; init; }

    /// <summary>
    /// Gets the response-body properties an exchange never keeps, at any depth: the account identifiers a provider echoes
    /// back, such as the <c>user_id</c> in OpenRouter's error bodies. A fixture is committed, so it must not name the
    /// account that recorded it.
    /// </summary>
    public static IReadOnlyList<string> DroppedProperties { get; } = ["user_id"];

    /// <summary>Gets whether <see cref="Status"/> is 2xx. It is read from the status, so a fixture does not keep it.</summary>
    [JsonIgnore]
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>Builds an exchange from what went over the wire.</summary>
    /// <param name="probe">The probe's id.</param>
    /// <param name="requestBody">The request body, or <see langword="null"/>.</param>
    /// <param name="status">The response status.</param>
    /// <param name="contentType">The response's <c>Content-Type</c>, or <see langword="null"/>.</param>
    /// <param name="headers">The allowlisted response headers, or <see langword="null"/>.</param>
    /// <param name="responseBody">The response body as text.</param>
    /// <returns>The exchange.</returns>
    public static ConformanceExchange FromHttp(
        string probe, string? requestBody, int status, string? contentType, IReadOnlyDictionary<string, string>? headers, string responseBody)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probe);
        ArgumentNullException.ThrowIfNull(responseBody);

        return new ConformanceExchange
        {
            Probe = probe,
            Request = requestBody is not null && TryParse(requestBody, out var request) ? request : null,
            Status = status,
            ContentType = contentType,
            Headers = headers is { Count: > 0 } ? headers : null,
            Response = IsJson(contentType) && TryParse(responseBody, out var json)
                ? WithoutDroppedProperties(json)
                : JsonSerializer.SerializeToElement(responseBody, ConformanceJsonContext.Default.String),
        };
    }

    /// <summary>Gets the first header of <paramref name="name"/>, in any letter case, or <see langword="null"/>.</summary>
    /// <param name="name">The header name.</param>
    /// <returns>Its value, or <see langword="null"/>.</returns>
    public string? Header(string name)
    {
        if (Headers is null)
        {
            return null;
        }

        foreach (var header in Headers)
        {
            if (string.Equals(header.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                return header.Value;
            }
        }

        return null;
    }

    /// <summary>Gets the response body as the text that was sent.</summary>
    /// <returns>The body, or an empty string when there is none.</returns>
    public string ResponseText()
        => Response is not { } body ? string.Empty
            : !IsJson(ContentType) && body.ValueKind == JsonValueKind.String ? body.GetString() ?? string.Empty
            : body.GetRawText();

    /// <summary>Gets the response body as JSON, when it is JSON.</summary>
    /// <param name="body">The body.</param>
    /// <returns><see langword="true"/> when <see cref="ContentType"/> is JSON and the body is not kept as text.</returns>
    public bool TryGetJson(out JsonElement body)
    {
        if (Response is { } response && IsJson(ContentType))
        {
            body = response;
            return true;
        }

        body = default;
        return false;
    }

    /// <summary>Gets whether a <c>Content-Type</c> is JSON: <c>application/json</c> or a <c>+json</c> type, parameters aside.</summary>
    /// <param name="contentType">The header value, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> for JSON.</returns>
    internal static bool IsJson(string? contentType)
    {
        if (contentType is null)
        {
            return false;
        }

        var semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
        var mediaType = (semicolon < 0 ? contentType : contentType[..semicolon]).Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase);
    }

    private static JsonElement WithoutDroppedProperties(JsonElement body)
    {
        var node = JsonNode.Parse(body.GetRawText());
        if (!Drop(node))
        {
            return body;
        }

        using var document = JsonDocument.Parse(node!.ToJsonString());
        return document.RootElement.Clone();
    }

    private static bool Drop(JsonNode? node)
    {
        var dropped = false;
        switch (node)
        {
            case JsonObject properties:
                foreach (var name in DroppedProperties)
                {
                    dropped |= properties.Remove(name);
                }

                foreach (var property in properties)
                {
                    dropped |= Drop(property.Value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    dropped |= Drop(item);
                }

                break;
        }

        return dropped;
    }

    private static bool TryParse(string text, out JsonElement element)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            element = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            element = default;
            return false;
        }
    }
}
