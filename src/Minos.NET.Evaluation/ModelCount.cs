namespace Minos.Evaluation;

/// <summary>How many served responses one model gave: successful ones and <c>InvalidResponse</c> ones.</summary>
public sealed class ModelCount
{
    internal ModelCount(string? model, int count)
    {
        Model = model;
        Count = count;
    }

    /// <summary>Gets the response model, or <see langword="null"/> for responses that named none.</summary>
    public string? Model { get; }

    /// <summary>Gets the number of responses.</summary>
    public int Count { get; }
}
