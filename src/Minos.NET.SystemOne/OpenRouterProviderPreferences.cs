namespace Minos.SystemOne;

/// <summary>
/// OpenRouter's provider routing preferences, sent with every request as OpenRouter's <c>provider</c> object. Set them on
/// <see cref="SystemOneDecisionClientOptions.ProviderPreferences"/> to require a guarantee per request instead of trusting the
/// account-wide privacy setting, which still applies as well.
/// </summary>
/// <remarks>
/// OpenRouter only: a client whose <see cref="SystemOneDecisionClientOptions.Provider"/> is another provider refuses to
/// start when any preference is on. When OpenRouter has no endpoint that meets them, the call fails with a
/// <see cref="DecisionError"/> carrying OpenRouter's status code, and it is not retried.
/// </remarks>
public sealed class OpenRouterProviderPreferences
{
    /// <summary>
    /// Gets or sets a value indicating whether only Zero Data Retention endpoints may serve a request. Sent as
    /// <c>"zdr": true</c>. Defaults to <see langword="false"/>, which sends nothing.
    /// </summary>
    public bool ZeroDataRetention { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether providers that collect user data are refused. Sent as
    /// <c>"data_collection": "deny"</c>. Defaults to <see langword="false"/>, which sends nothing.
    /// </summary>
    public bool DenyDataCollection { get; set; }
}
