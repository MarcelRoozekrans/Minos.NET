using System.Globalization;
using System.Text.Json;

namespace Minos;

/// <summary>
/// The options of a keyed Choice or the levels of a keyed Score, in wire order. Keys are looked up by a linear,
/// allocation-free scan, <see cref="Utf8Keys.IndexOf"/>.
/// </summary>
internal sealed class KeyedOptionSet : IJevOptionKeys
{
    private readonly string[] _keys;
    private readonly byte[][] _utf8Keys;

    /// <summary>Initializes a new instance of the <see cref="KeyedOptionSet"/> class.</summary>
    /// <param name="keys">The wire keys, in wire order. The set keeps the array.</param>
    /// <remarks>
    /// Duplicate and empty keys are not checked here: the builder's validation rejects both before a set is created.
    /// Should a duplicate get through, the first match wins.
    /// </remarks>
    public KeyedOptionSet(string[] keys)
    {
        _keys = keys;
        _utf8Keys = Utf8Keys.Encode(keys);
    }

    /// <summary>Gets the number of options.</summary>
    public int Count => _keys.Length;

    /// <summary>Gets the key at <paramref name="index"/>.</summary>
    /// <param name="index">The option's position in wire order.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid position.</exception>
    public string this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _keys.Length);
            return _keys[index];
        }
    }

    /// <summary>Creates the levels of a keyed Score: keys <c>"0"</c> to <c>count - 1</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    public static KeyedOptionSet Levels(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var keys = new string[count];
        for (var i = 0; i < count; i++)
        {
            keys[i] = i.ToString(CultureInfo.InvariantCulture);
        }

        return new KeyedOptionSet(keys);
    }

    /// <summary>Returns the position of <paramref name="key"/>, compared ordinally, or -1.</summary>
    public int IndexOf(string key)
    {
        for (var i = 0; i < _keys.Length; i++)
        {
            if (string.Equals(_keys[i], key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public int IndexOfKey(ref Utf8JsonReader reader) => Utf8Keys.IndexOf(ref reader, _utf8Keys);
}
