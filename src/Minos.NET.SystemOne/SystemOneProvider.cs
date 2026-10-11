namespace Minos.SystemOne;

/// <summary>Where the client sends requests.</summary>
public enum SystemOneProvider
{
    /// <summary>TypeSafe's own API at <c>https://api.typesafe.ai/</c>, with a TypeSafe API key.</summary>
    TypeSafe,

    /// <summary>OpenRouter's System One API at <c>https://openrouter.ai/api/</c>, with an OpenRouter API key.</summary>
    OpenRouter,

    /// <summary>
    /// Cloudflare Workers AI, at <c>https://api.cloudflare.com/client/v4/accounts/{AccountId}/ai/run/@cf/cloudflare/{model}</c>,
    /// with a Cloudflare API token. It needs <see cref="SystemOneClientOptions.AccountId"/> and
    /// <see cref="SystemOneClientOptions.Model"/>.
    /// </summary>
    CloudflareWorkersAi,

    /// <summary>
    /// A vLLM Semantic Router you run, at <see cref="SystemOneClientOptions.BaseAddress"/>. A key is sent only when
    /// <see cref="SystemOneClientOptions.ApiKey"/> is set. It needs <see cref="SystemOneClientOptions.Model"/>.
    /// </summary>
    VllmSemanticRouter,

    /// <summary>
    /// Any other server that speaks the <c>/v1/systemone</c> protocol, at <see cref="SystemOneClientOptions.BaseAddress"/>
    /// and <see cref="SystemOneClientOptions.Path"/>. A key is sent only when <see cref="SystemOneClientOptions.ApiKey"/> is
    /// set. It needs <see cref="SystemOneClientOptions.Model"/>.
    /// </summary>
    Custom,
}
