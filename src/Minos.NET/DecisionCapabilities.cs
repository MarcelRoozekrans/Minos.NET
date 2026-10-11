namespace Minos;

/// <summary>
/// What a provider accepts, checked before a request is sent. Each property left <see langword="null"/> takes the
/// configured provider's default; a set one wins, so a server that accepts Noul questions only can say so. A protocol
/// package's client options take one, and its documentation lists each provider's defaults.
/// </summary>
/// <remarks>
/// <para>
/// A neutral, typed or built-set call that breaks one is not sent: it returns a <see cref="DecisionError"/> of kind
/// <see cref="DecisionErrorKind.Unsupported"/>, with one <see cref="QuestionFailure"/> per problem in
/// <see cref="DecisionError.Failures"/>: MIN202 for more questions than <see cref="MaxQuestions"/>, then, in question order,
/// MIN201 for a question kind not in <see cref="QuestionKinds"/> and MIN203 for a Choice question with more options than
/// <see cref="MaxChoiceOptions"/>, then MIN206 for images to a provider without <see cref="Images"/>. Its message is the
/// first failure's. A protocol package's raw call is not checked.
/// </para>
/// <para>
/// A client's <c>GetService</c> returns the resolved values, every property set, as a new instance on each call.
/// </para>
/// </remarks>
public sealed class DecisionCapabilities
{
    /// <summary>
    /// Gets or sets the question kinds the provider accepts. When set, it must allow at least one kind and name no flag
    /// outside <see cref="Minos.QuestionKinds.All"/>.
    /// </summary>
    public QuestionKinds? QuestionKinds { get; set; }

    /// <summary>Gets or sets the most questions one request may ask. When set, it must be at least 1.</summary>
    public int? MaxQuestions { get; set; }

    /// <summary>Gets or sets the most options one Choice question may have. When set, it must be at least 2.</summary>
    public int? MaxChoiceOptions { get; set; }

    /// <summary>
    /// Gets or sets whether the provider accepts images. A request with images to a provider without them returns
    /// Unsupported with MIN206.
    /// </summary>
    public bool? Images { get; set; }

    /// <summary>
    /// Gets or sets whether the provider serves a model listing. Without it, a client's model listing returns
    /// <see cref="DecisionErrorKind.Unsupported"/> without sending a request.
    /// </summary>
    public bool? ModelListing { get; set; }
}
