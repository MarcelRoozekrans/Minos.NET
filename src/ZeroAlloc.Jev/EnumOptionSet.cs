using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ZeroAlloc.Jev.Generator;

namespace ZeroAlloc.Jev;

/// <summary>
/// The options or levels of an enum question built at run time, read from the enum's public static fields in
/// declaration order, as the generator reads the enum's members.
/// </summary>
/// <typeparam name="T">
/// The enum. Its public fields are declared through <see cref="DynamicallyAccessedMembersAttribute"/>, so the trimmer
/// keeps them and reading them is trim- and Native AOT-safe.
/// </typeparam>
/// <remarks>
/// <see cref="ForChoice"/> holds one option per distinct value, in declaration order, each keyed by its member name in
/// snake_case, by the generator's own <c>SnakeCase</c>. An alias, a later field repeating an earlier field's value, is
/// skipped, so a value is keyed by its first declared name. <see cref="Enum.GetName{TEnum}(TEnum)"/> is not used: for
/// an aliased value in a larger enum it may return the alias. A Score's levels follow the order its builder gave them,
/// from <see cref="Levels"/>, keyed by index. Attributes on the members, such as <c>[Criteria(Key = …)]</c> or
/// <c>[Level]</c>, are not read.
/// </remarks>
internal sealed class EnumOptionSet<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] T> : JevOptionSet<T>
    where T : struct, Enum
{
    // Created on first use; a race creates two equal sets and keeps one, which is harmless.
    private static EnumOptionSet<T>? s_forChoice;

    private readonly T[] _values;
    private readonly string[] _names;
    private readonly string[] _keys;
    private readonly byte[][] _utf8Keys;

    private EnumOptionSet(T[] values, string[] names, string[] keys)
    {
        _values = values;
        _names = names;
        _keys = keys;
        _utf8Keys = Utf8Keys.Encode(keys);
    }

    /// <summary>
    /// Gets the options of a Choice over <typeparamref name="T"/>, in declaration order, keyed by member name in snake_case.
    /// </summary>
    public static EnumOptionSet<T> ForChoice => s_forChoice ??= Create();

    public override int Count => _values.Length;

    public override T this[int index]
        => (uint)index < (uint)_values.Length ? _values[index] : throw new ArgumentOutOfRangeException(nameof(index));

    /// <summary>Gets the wire key at <paramref name="index"/>.</summary>
    public string KeyAt(int index) => _keys[index];

    /// <summary>Gets the member name at <paramref name="index"/>, for messages.</summary>
    public string NameAt(int index) => _names[index];

    /// <summary>Creates a Score's levels, in the order given, keyed <c>"0"</c>, <c>"1"</c>, ….</summary>
    /// <param name="members">
    /// For each level, lowest first, the index in this set of the member it is. Each entry must be a valid
    /// <see cref="ForChoice"/> index and every member must appear exactly once; the builder's validation, JEV104 for a
    /// missing member and JEV106 for one given twice, rejects anything else before a set's levels are created.
    /// </param>
    /// <returns>The levels.</returns>
    public EnumOptionSet<T> Levels(ReadOnlySpan<int> members)
    {
        var values = new T[members.Length];
        var names = new string[members.Length];
        var keys = new string[members.Length];
        var level = 0;
        foreach (ref readonly var member in members)
        {
            values[level] = _values[member];
            names[level] = _names[member];
            keys[level] = level.ToString(CultureInfo.InvariantCulture);
            level++;
        }

        return new EnumOptionSet<T>(values, names, keys);
    }

    public override int IndexOf(T value)
    {
        var comparer = EqualityComparer<T>.Default;
        for (var i = 0; i < _values.Length; i++)
        {
            if (comparer.Equals(_values[i], value))
            {
                return i;
            }
        }

        return -1;
    }

    public override int IndexOfKey(ref Utf8JsonReader reader) => Utf8Keys.IndexOf(ref reader, _utf8Keys);

    private static EnumOptionSet<T> Create()
    {
        // GetFields documents no order; metadata order, which is declaration order, is relied on. The declaration-order
        // and generator-parity tests in EnumOptionSetTests guard it. An enum's public static fields are its members, in
        // the generator reads them in.
        var fields = typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static);
        var values = new List<T>(fields.Length);
        var names = new List<string>(fields.Length);
        foreach (var field in fields)
        {
            var value = (T)field.GetValue(null)!;

            // An alias repeats an earlier field's value; the earlier member already represents it.
            if (!values.Contains(value))
            {
                values.Add(value);
                names.Add(field.Name);
            }
        }

        var keys = new string[names.Count];
        for (var i = 0; i < keys.Length; i++)
        {
            keys[i] = SnakeCase.Convert(names[i]);
        }

        return new EnumOptionSet<T>([.. values], [.. names], keys);
    }
}
