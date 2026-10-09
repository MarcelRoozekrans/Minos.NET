using System.Text.Json;

namespace Minos;

/// <summary>The wire keys of a question's options or levels, in wire order: all the answer readers need.</summary>
internal interface IJevOptionKeys
{
    /// <summary>Gets the number of options.</summary>
    int Count { get; }

    /// <summary>Returns the position of the option whose wire key is the reader's current token, or -1.</summary>
    /// <param name="reader">A reader positioned on a <see cref="JsonTokenType.PropertyName"/> or <see cref="JsonTokenType.String"/> token.</param>
    /// <returns>The position, or -1 when no option has that key.</returns>
    int IndexOfKey(ref Utf8JsonReader reader);
}
