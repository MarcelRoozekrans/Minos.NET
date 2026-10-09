using System.Diagnostics.CodeAnalysis;

namespace Minos;

/// <summary>The probability of each option of a keyed Choice answer, or each level of a keyed Score answer.</summary>
/// <remarks>
/// A view over a buffer that all answers of one parse share, so reading it allocates nothing. Options the response did
/// not mention have probability 0.
/// </remarks>
public readonly struct KeyedProbabilityMap : IEquatable<KeyedProbabilityMap>
{
    private readonly double[]? _buffer;
    private readonly int _offset;
    private readonly KeyedOptionSet? _options;

    internal KeyedProbabilityMap(double[] buffer, int offset, KeyedOptionSet options)
    {
        _buffer = buffer;
        _offset = offset;
        _options = options;
    }

    /// <summary>Gets the number of options or levels.</summary>
    public int Count => _options?.Count ?? 0;

    /// <summary>Gets the probability of the option or level with wire key <paramref name="key"/>.</summary>
    /// <param name="key">The key: an option's key, or a level's index such as <c>"0"</c>.</param>
    /// <returns>The probability, between 0 and 1.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="key"/> is not one of the keys.</exception>
    public double this[string key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            var index = _options?.IndexOf(key) ?? -1;
            if (index < 0)
            {
                ThrowOutOfRange(nameof(key), "The key is not one of the options.");
            }

            return _buffer![_offset + index];
        }
    }

    /// <summary>Gets the probability at <paramref name="index"/> in wire order: for a Score, the level index.</summary>
    /// <param name="index">The position.</param>
    /// <returns>The probability, between 0 and 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is not a valid position.</exception>
    public double this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                ThrowOutOfRange(nameof(index), "The index is not a valid position.");
            }

            return _buffer![_offset + index];
        }
    }

    /// <summary>Compares two maps for equality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values are equal.</returns>
    public static bool operator ==(KeyedProbabilityMap left, KeyedProbabilityMap right) => left.Equals(right);

    /// <summary>Compares two maps for inequality.</summary>
    /// <param name="left">The first value.</param>
    /// <param name="right">The second value.</param>
    /// <returns><see langword="true"/> when the values differ.</returns>
    public static bool operator !=(KeyedProbabilityMap left, KeyedProbabilityMap right) => !left.Equals(right);

    /// <summary>Returns an enumerator over the keys and their probabilities, in wire order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <inheritdoc />
    public bool Equals(KeyedProbabilityMap other)
    {
        var count = Count;
        if (count != other.Count)
        {
            return false;
        }

        for (var i = 0; i < count; i++)
        {
            if (!ReferenceEquals(_options, other._options)
                && !string.Equals(_options![i], other._options![i], StringComparison.Ordinal))
            {
                return false;
            }

            if (!_buffer![_offset + i].Equals(other._buffer![other._offset + i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is KeyedProbabilityMap other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        var count = Count;
        hash.Add(count);
        for (var i = 0; i < count; i++)
        {
            hash.Add(_buffer![_offset + i]);
        }

        return hash.ToHashCode();
    }

    [DoesNotReturn]
    private static void ThrowOutOfRange(string paramName, string message) => throw new ArgumentOutOfRangeException(paramName, message);

    /// <summary>Enumerates the keys of a <see cref="KeyedProbabilityMap"/> with their probabilities.</summary>
    public struct Enumerator
    {
        private readonly KeyedProbabilityMap _map;
        private int _index;

        internal Enumerator(KeyedProbabilityMap map)
        {
            _map = map;
            _index = -1;
        }

        /// <summary>Gets the current key and its probability.</summary>
        public readonly (string Key, double Probability) Current
            => (_map._options![_index], _map._buffer![_map._offset + _index]);

        /// <summary>Advances to the next key.</summary>
        /// <returns><see langword="true"/> when there is a current key.</returns>
        public bool MoveNext() => ++_index < _map.Count;
    }
}
