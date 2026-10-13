namespace Minos.SystemOne;

/// <summary>Where the client sends requests.</summary>
public enum SystemOneProvider
{
    /// <summary>TypeSafe's own API at <c>https://api.typesafe.ai/</c>, with a TypeSafe API key.</summary>
    TypeSafe,

    /// <summary>OpenRouter's System One API at <c>https://openrouter.ai/api/</c>, with an OpenRouter API key.</summary>
    OpenRouter,

    /// <summary>
    /// A vLLM Semantic Router you run, at <see cref="SystemOneDecisionClientOptions.BaseAddress"/>. A key is sent only when
    /// <see cref="SystemOneDecisionClientOptions.ApiKey"/> is set. It needs <see cref="SystemOneDecisionClientOptions.Model"/>.
    /// </summary>
    VllmSemanticRouter,

    /// <summary>
    /// Any other server that speaks the <c>/v1/systemone</c> protocol, at <see cref="SystemOneDecisionClientOptions.BaseAddress"/>
    /// and <see cref="SystemOneDecisionClientOptions.Path"/>. A key is sent only when <see cref="SystemOneDecisionClientOptions.ApiKey"/> is
    /// set. It needs <see cref="SystemOneDecisionClientOptions.Model"/>.
    /// </summary>
    Custom,
}
