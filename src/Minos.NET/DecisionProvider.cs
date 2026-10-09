namespace Minos;

/// <summary>Where the client sends requests.</summary>
public enum DecisionProvider
{
    /// <summary>TypeSafe's own API at <c>https://api.typesafe.ai/</c>, with a TypeSafe API key.</summary>
    TypeSafe,

    /// <summary>OpenRouter's System One API at <c>https://openrouter.ai/api/</c>, with an OpenRouter API key.</summary>
    OpenRouter,
}
