namespace Minos;

/// <summary>A model id or alias the account can send in <see cref="SystemOneRequest.Model"/>.</summary>
public sealed record ModelCard
{
    /// <summary>Gets the model id or alias.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what the model is for.</summary>
    public required string Description { get; init; }

    /// <summary>Gets when the model or alias was released, as reported by the API.</summary>
    public required string ReleaseDate { get; init; }
}
