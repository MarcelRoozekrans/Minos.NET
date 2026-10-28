namespace Minos.Extensions.AI;

/// <summary>Configures a <see cref="ChatDecisionClient"/>. Every property is optional.</summary>
/// <remarks>
/// A client reads these options once, when it is constructed. Changing this instance afterwards has no effect on a
/// client already built.
/// </remarks>
public sealed class ChatDecisionClientOptions
{
    /// <summary>Gets or sets how each answer's probabilities are made. Defaults to <see cref="ProbabilityMode.Auto"/>.</summary>
    public ProbabilityMode ProbabilityMode { get; set; } = ProbabilityMode.Auto;

    /// <summary>
    /// Gets or sets the model that requests without their own <see cref="DecisionRequest.Model"/> ask. When unset, the
    /// chat client's own default model is used. Surrounding whitespace is trimmed; a blank value is rejected.
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// Gets or sets what the bridge accepts, checked before anything is sent. Each property left unset takes the bridge's
    /// default: every question kind, no question limit, at most 26 options per Choice question, no images, and no model
    /// listing. See <see cref="DecisionCapabilities"/> for the check.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Options are labelled with the letters A to Z, so <see cref="DecisionCapabilities.MaxChoiceOptions"/> may not be more
    /// than 26. Set <see cref="DecisionCapabilities.SupportsImages"/> only when the chat model reads images.
    /// </para>
    /// <para>
    /// <see cref="DecisionCapabilities.SupportsModelListing"/> has no effect: the bridge has no model listing, so it is
    /// always <see langword="false"/>, and <c>GetService&lt;DecisionCapabilities&gt;()</c> reports it so whatever is set here.
    /// </para>
    /// </remarks>
    public DecisionCapabilities? Capabilities { get; set; }
}
