using System.Runtime.InteropServices;

namespace ZeroAlloc.Jev.Telemetry;

/// <summary>
/// The raw path's per-answer confidences: each <see cref="ChoiceAnswer"/>'s and <see cref="ScoreAnswer"/>'s, none for a
/// <see cref="NoulAnswer"/>. A struct enumerable, iterated by the proxy's per-element histogram.
/// </summary>
/// <param name="answers">The response's answers.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly struct AnswerConfidences(IReadOnlyDictionary<string, JevAnswer> answers)
{
    /// <summary>Starts an enumeration.</summary>
    public Enumerator GetEnumerator() => new(answers);

    /// <summary>
    /// Enumerates the confidences. System.Text.Json deserializes <c>IReadOnlyDictionary</c> into a
    /// <see cref="Dictionary{TKey, TValue}"/>, whose struct enumerator allocates nothing; any other dictionary is read
    /// through its interface enumerator, which allocates.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Enumerator : IDisposable
    {
        private readonly IEnumerator<JevAnswer>? _other;
        private readonly bool _isDictionary;
        private Dictionary<string, JevAnswer>.ValueCollection.Enumerator _values;
        private double _current;

        public Enumerator(IReadOnlyDictionary<string, JevAnswer> answers)
        {
            if (answers is Dictionary<string, JevAnswer> dictionary)
            {
                _isDictionary = true;
                _values = dictionary.Values.GetEnumerator();
            }
            else
            {
                _other = answers.Values.GetEnumerator();
            }
        }

        /// <summary>Gets the current confidence.</summary>
        public readonly double Current => _current;

        /// <summary>Moves to the next Choice or Score answer's confidence.</summary>
        public bool MoveNext()
        {
            while (_isDictionary ? _values.MoveNext() : _other!.MoveNext())
            {
                var answer = _isDictionary ? _values.Current : _other!.Current;
                switch (answer)
                {
                    case ChoiceAnswer choice:
                        _current = choice.Confidence;
                        return true;
                    case ScoreAnswer score:
                        _current = score.Confidence;
                        return true;
                }
            }

            return false;
        }

        /// <summary>Releases the interface enumerator, when one was used.</summary>
        public readonly void Dispose() => _other?.Dispose();
    }
}
