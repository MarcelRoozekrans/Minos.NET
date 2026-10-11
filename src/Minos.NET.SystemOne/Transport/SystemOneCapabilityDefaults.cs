using Minos.Transport;

namespace Minos.SystemOne.Transport;

/// <summary>Each <see cref="SystemOneProvider"/> preset's default <see cref="DecisionCapabilities"/>.</summary>
internal static class SystemOneCapabilityDefaults
{
    /// <summary>Gets a preset's defaults.</summary>
    /// <param name="provider">The preset.</param>
    /// <returns>What the preset's vendor accepts.</returns>
    internal static CapabilitySet For(SystemOneProvider provider) => provider switch
    {
        // 255 options per Choice question is the limit TypeSafe's API page documents, https://docs.typesafe.ai/api.
        // OpenRouter passes requests through to TypeSafe's model, so it has the same limit.
        SystemOneProvider.TypeSafe => new(QuestionKinds.All, null, 255, Images: false, ModelListing: true),
        SystemOneProvider.OpenRouter => new(QuestionKinds.All, null, 255, Images: false, ModelListing: false),
        SystemOneProvider.VllmSemanticRouter => new(QuestionKinds.All, null, null, Images: false, ModelListing: false),
        _ => new(QuestionKinds.All, null, null, Images: false, ModelListing: false),
    };
}
