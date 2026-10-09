using System.Runtime.InteropServices;
using System.Text.Json;

namespace Minos.Telemetry;

/// <summary>
/// Each answer's <c>confidence</c> in a <c>/v1/systemone</c> response body, in response order: one per Choice and Score
/// answer, none for a Noul. A struct enumerable, so the proxy's per-element histogram iterates it without allocating.
/// </summary>
/// <param name="response">The whole response body, which must stay unreturned while this is enumerated.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly struct ConfidenceValues(ReadOnlyMemory<byte> response)
{
    /// <summary>Starts an enumeration.</summary>
    public Enumerator GetEnumerator() => new(response);

    /// <summary>
    /// Enumerates the confidences. A plain struct, not a <c>ref struct</c>, which ZeroAlloc.Telemetry's per-element
    /// histogram requires: it keeps where the last reader stopped, as an offset and a <see cref="JsonReaderState"/>, and
    /// resumes a new <see cref="Utf8JsonReader"/> there on each <see cref="MoveNext"/>.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    internal struct Enumerator
    {
        private readonly ReadOnlyMemory<byte> _json;
        private JsonReaderState _state;
        private int _offset;
        private bool _started;
        private bool _finished;
        private double _current;

        public Enumerator(ReadOnlyMemory<byte> json)
        {
            _json = json;
            _offset = json.Length - TypedEvaluation.SkipUtf8Bom(json.Span).Length;
        }

        /// <summary>Gets the current confidence.</summary>
        public readonly double Current => _current;

        /// <summary>Moves to the next Choice or Score answer's confidence.</summary>
        public bool MoveNext()
        {
            if (_finished)
            {
                return false;
            }

            var reader = new Utf8JsonReader(_json.Span[_offset..], isFinalBlock: true, _state);
            if (!_started)
            {
                _started = true;
                if (!ResponseFields.TryReadTopLevel(ref reader, "answers"u8) || reader.TokenType != JsonTokenType.StartObject)
                {
                    _finished = true;
                    return false;
                }
            }

            // Each answer: a property name, then its value; the loop ends at the answers object's end.
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                reader.Read();
                if (TryReadConfidence(ref reader, out var confidence))
                {
                    _current = confidence;
                    _offset += (int)reader.BytesConsumed;
                    _state = reader.CurrentState;
                    return true;
                }
            }

            _finished = true;
            return false;
        }

        // Reads one answer value to its end. Only the answer's own top-level "confidence" counts: nested objects, such
        // as probabilities with an option named "confidence", are skipped whole.
        private static bool TryReadConfidence(ref Utf8JsonReader reader, out double confidence)
        {
            confidence = 0;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                return false;
            }

            var found = false;
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                var isConfidence = !found && reader.ValueTextEquals("confidence"u8);
                reader.Read();
                if (isConfidence && reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out confidence))
                {
                    found = true;
                    continue;
                }

                reader.Skip();
            }

            return found;
        }
    }
}
