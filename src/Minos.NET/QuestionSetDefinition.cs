using System.Collections.ObjectModel;

namespace Minos;

/// <summary>
/// A question set's questions, independent of any provider's wire format. Generated <c>[Questions]</c> types and
/// <see cref="QuestionSet"/> expose one; a protocol turns it into a request and reads its answers back.
/// </summary>
/// <remarks>Immutable and safe to share across threads.</remarks>
public sealed class QuestionSetDefinition
{
    // One cache slot per protocol; SystemOneProtocol uses slot 0.
    private readonly object?[] _protocolData = new object?[1];

    /// <summary>Creates a definition from <paramref name="questions"/>, in wire order.</summary>
    /// <param name="questions">The questions.</param>
    /// <exception cref="ArgumentException">A question is <see langword="null"/>, or two share a key.</exception>
    public QuestionSetDefinition(params ReadOnlySpan<QuestionDefinition> questions)
    {
        var array = questions.ToArray();
        var keys = new string[array.Length];
        var optionKeys = new byte[array.Length][][];
        var offsets = new int[array.Length];
        var offset = 0;
        for (var i = 0; i < array.Length; i++)
        {
            var question = array[i] ?? throw new ArgumentException("A question is null.", nameof(questions));
            if (Array.IndexOf(keys, question.Key, 0, i) >= 0)
            {
                throw new ArgumentException($"Two questions share the key '{question.Key}'.", nameof(questions));
            }

            keys[i] = question.Key;
            var options = question.OptionArray;
            var optionKeyStrings = new string[options.Length];
            for (var j = 0; j < options.Length; j++)
            {
                optionKeyStrings[j] = options[j].Key;
            }

            optionKeys[i] = Utf8Keys.Encode(optionKeyStrings);
            offsets[i] = offset;
            offset += options.Length;
        }

        QuestionArray = array;
        Questions = array.Length == 0 ? [] : new ReadOnlyCollection<QuestionDefinition>(array);
        KeysUtf8 = Utf8Keys.Encode(keys);
        OptionKeysUtf8 = optionKeys;
        Offsets = offsets;
        ProbabilityCount = offset;
    }

    /// <summary>Gets the questions, in wire order.</summary>
    public IReadOnlyList<QuestionDefinition> Questions { get; }

    internal QuestionDefinition[] QuestionArray { get; }

    internal byte[][] KeysUtf8 { get; }

    internal byte[][][] OptionKeysUtf8 { get; }

    internal int[] Offsets { get; }

    internal int ProbabilityCount { get; }

    internal TData GetOrAddProtocolData<TData>(int slot, Func<QuestionSetDefinition, TData> create)
        where TData : class
    {
        if (Volatile.Read(ref _protocolData[slot]) is TData existing)
        {
            return existing;
        }

        var created = create(this);
        return Interlocked.CompareExchange(ref _protocolData[slot], created, null) as TData ?? created;
    }
}
