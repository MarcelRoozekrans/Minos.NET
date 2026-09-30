using System.Text.Json;

namespace ZeroAlloc.Jev;

/// <summary>The options of a typed Choice question or the levels of a typed Score question, in wire order.</summary>
/// <typeparam name="T">The enum whose members are the options or levels.</typeparam>
/// <remarks>
/// The <c>[JevQuestions]</c> source generator emits one sealed subclass per question. It maps enum values, indices and
/// wire keys with switches, so no reflection is involved.
/// </remarks>
public abstract class JevOptionSet<T> : IJevOptionKeys
    where T : struct, Enum
{
    /// <summary>Gets the number of options.</summary>
    public abstract int Count { get; }

    /// <summary>Gets the option at <paramref name="index"/>.</summary>
    /// <param name="index">The option's position in wire order.</param>
    /// <returns>The option.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid position.</exception>
    public abstract T this[int index] { get; }

    /// <summary>Returns the position of <paramref name="value"/> in wire order.</summary>
    /// <param name="value">The enum value.</param>
    /// <returns>The position, or -1 when <paramref name="value"/> is not an option.</returns>
    public abstract int IndexOf(T value);

    /// <summary>Returns the position of the option whose wire key is the reader's current token.</summary>
    /// <param name="reader">A reader positioned on a <see cref="JsonTokenType.PropertyName"/> or <see cref="JsonTokenType.String"/> token.</param>
    /// <returns>The position, or -1 when no option has that key.</returns>
    public abstract int IndexOfKey(ref Utf8JsonReader reader);
}
