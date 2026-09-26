namespace Jev.Net;

/// <summary>The probability of each option of a typed Choice or Score answer, looked up by enum value.</summary>
/// <typeparam name="T">The enum whose members are the options or levels.</typeparam>
/// <remarks>
/// The map is a view over a buffer that all answers of one parse share, so reading it allocates nothing. Options the
/// response did not mention have probability 0.
/// </remarks>
public readonly struct ProbabilityMap<T>
    where T : struct, Enum
{
    private readonly double[]? _buffer;
    private readonly int _offset;
    private readonly JevOptionSet<T>? _options;

    /// <summary>Initializes a new instance of the <see cref="ProbabilityMap{T}"/> struct.</summary>
    /// <param name="buffer">The buffer holding the probabilities.</param>
    /// <param name="offset">The position in <paramref name="buffer"/> of the first option's probability.</param>
    /// <param name="options">The options, in buffer order.</param>
    /// <exception cref="ArgumentNullException"><paramref name="buffer"/> or <paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The options do not fit in <paramref name="buffer"/> from <paramref name="offset"/>.</exception>
    public ProbabilityMap(double[] buffer, int offset, JevOptionSet<T> options)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, buffer.Length - options.Count);

        _buffer = buffer;
        _offset = offset;
        _options = options;
    }

    /// <summary>Gets the number of options.</summary>
    public int Count => _options?.Count ?? 0;

    /// <summary>Gets the probability of <paramref name="option"/>.</summary>
    /// <param name="option">The option.</param>
    /// <returns>The probability, between 0 and 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="option"/> is not one of the options.</exception>
    public double this[T option]
    {
        get
        {
            var index = _options?.IndexOf(option) ?? -1;
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(option), option, "The value is not one of the options.");
            }

            return _buffer![_offset + index];
        }
    }

    /// <summary>Returns an enumerator over the options and their probabilities, in wire order.</summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(this);

    /// <summary>Enumerates the options of a <see cref="ProbabilityMap{T}"/> with their probabilities.</summary>
    public struct Enumerator
    {
        private readonly ProbabilityMap<T> _map;
        private int _index;

        internal Enumerator(ProbabilityMap<T> map)
        {
            _map = map;
            _index = -1;
        }

        /// <summary>Gets the current option and its probability.</summary>
        public readonly (T Option, double Probability) Current
            => (_map._options![_index], _map._buffer![_map._offset + _index]);

        /// <summary>Advances to the next option.</summary>
        /// <returns><see langword="true"/> when there is a current option.</returns>
        public bool MoveNext() => ++_index < _map.Count;
    }
}
