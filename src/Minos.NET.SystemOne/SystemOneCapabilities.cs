namespace Minos.SystemOne;

/// <summary>
/// What a provider accepts, checked before a request is sent. Each property left <see langword="null"/> takes the
/// <see cref="SystemOneClientOptions.Provider"/> preset's default; a set one wins, so a server that accepts Noul questions
/// only can say so.
/// </summary>
/// <remarks>
/// <para>The preset defaults:</para>
/// <list type="bullet">
/// <item><description><see cref="SystemOneProvider.TypeSafe"/>: every kind, no question limit, 255 Choice options, no
/// images, model listing.</description></item>
/// <item><description><see cref="SystemOneProvider.OpenRouter"/>: every kind, no question limit, 255 Choice options, no
/// images, no model listing.</description></item>
/// <item><description><see cref="SystemOneProvider.VllmSemanticRouter"/>: every kind, no question or option limit, no
/// images, no model listing.</description></item>
/// <item><description><see cref="SystemOneProvider.Custom"/>: every kind, no question or option limit, no images, no
/// model listing, because a self-hosted server's <c>GET v1/models</c> need not use TypeSafe's shape.</description></item>
/// </list>
/// <para>
/// A neutral, typed or built-set call whose question set breaks one is not sent: it returns a
/// <see cref="DecisionError"/> of kind <see cref="DecisionErrorKind.Unsupported"/>, with one
/// <see cref="QuestionFailure"/> per problem in <see cref="DecisionError.Failures"/>: MIN201 for a question kind not in
/// <see cref="QuestionKinds"/>, MIN202 for more questions than <see cref="MaxQuestions"/> and MIN203 for a Choice question
/// with more options than <see cref="MaxChoiceOptions"/>. The raw
/// <see cref="SystemOneClient.EvaluateAsync(SystemOneRequest, CancellationToken)"/> call is not checked.
/// <see cref="SystemOneClient.GetService"/> returns the resolved values, every property set, as a new instance on each call.
/// </para>
/// </remarks>
public sealed class SystemOneCapabilities
{
    /// <summary>
    /// Gets or sets the question kinds the provider accepts. When set, it must allow at least one kind and name no flag
    /// outside <see cref="SystemOneQuestionKinds.All"/>.
    /// </summary>
    public SystemOneQuestionKinds? QuestionKinds { get; set; }

    /// <summary>Gets or sets the most questions one request may ask. When set, it must be at least 1.</summary>
    public int? MaxQuestions { get; set; }

    /// <summary>Gets or sets the most options one Choice question may have. When set, it must be at least 2.</summary>
    public int? MaxChoiceOptions { get; set; }

    /// <summary>Gets or sets whether the provider accepts images in the state. Declared now; not checked yet.</summary>
    public bool? Images { get; set; }

    /// <summary>
    /// Gets or sets whether the provider serves <c>GET v1/models</c>. Without it,
    /// <see cref="SystemOneClient.ListModelsAsync(CancellationToken)"/> returns <see cref="DecisionErrorKind.Unsupported"/>
    /// without sending a request.
    /// </summary>
    public bool? ModelListing { get; set; }
}
