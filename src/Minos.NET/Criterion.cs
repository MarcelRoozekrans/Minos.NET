using System.Collections.ObjectModel;

namespace Minos;

/// <summary>
/// Describes a Choice option or a Score level: a plain text, a text with <see cref="WithExamples"/> and
/// <see cref="WithNotFor"/> texts, or JSON. <see cref="QuestionSetBuilder"/> takes one per option or level, and an
/// <see cref="OptionDefinition"/> carries one, in a built set's definition and in a generated set's alike.
/// </summary>
/// <remarks>
/// A text with examples or not-for texts is sent as the criterion object <c>{"description", "examples", "not_for"}</c>,
/// the same Minos.NET convention <c>[Criteria]</c> and <c>[Level]</c> use: an empty list and <see langword="null"/>
/// entries are left out, and a text with neither is sent as a plain JSON string. Immutable: each <c>With</c> method
/// returns a new criterion.
/// </remarks>
public sealed class Criterion
{
    private readonly string? _description;
    private readonly DecisionContent _json;
    private readonly string?[] _examples;
    private readonly string?[] _notFor;

    private Criterion(string? description, DecisionContent json, string?[] examples, string?[] notFor)
    {
        _description = description;
        _json = json;
        _examples = examples;
        _notFor = notFor;
        Examples = Texts(examples);
        NotFor = Texts(notFor);
    }

    /// <summary>Gets what the option or level means, for a text criterion; <see langword="null"/> for a JSON one.</summary>
    public string? Description => _description;

    /// <summary>Gets the JSON object or array, for a JSON criterion; <see langword="null"/> for a text one.</summary>
    public DecisionContent? JsonContent => _description is null ? _json : (DecisionContent?)null;

    /// <summary>
    /// Gets the texts that belong to the option or level, in the order given, without the <see langword="null"/>
    /// entries: exactly the entries that are sent. Empty when there are none, and always for a JSON criterion.
    /// </summary>
    public IReadOnlyList<string> Examples { get; }

    /// <summary>
    /// Gets the texts that do not belong to the option or level, in the order given, without the
    /// <see langword="null"/> entries: exactly the entries that are sent. Empty when there are none, and always for a
    /// JSON criterion.
    /// </summary>
    public IReadOnlyList<string> NotFor { get; }

    // The entries as given, null ones included, for the builder's blank-entry warning.
    internal ReadOnlySpan<string?> ExampleEntries => _examples;

    internal ReadOnlySpan<string?> NotForEntries => _notFor;

    /// <summary>Creates a criterion from a plain text.</summary>
    /// <param name="description">What the option or level means.</param>
    /// <returns>The criterion.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    public static Criterion Text(string description)
    {
        ArgumentNullException.ThrowIfNull(description);
        return new Criterion(description, default, [], []);
    }

    /// <summary>Creates a criterion sent as a JSON object or array.</summary>
    /// <param name="json">Structured content, from <see cref="DecisionContent.FromUtf8Json(ReadOnlySpan{byte})"/> for one.</param>
    /// <returns>The criterion.</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> is text or uninitialized.</exception>
    public static Criterion Json(DecisionContent json)
    {
        if (!json.TryGetJson(out _))
        {
            throw new ArgumentException("A JSON criterion needs a JSON object or array; use Text for plain text.", nameof(json));
        }

        return new Criterion(null, json, [], []);
    }

    /// <summary>Returns this text criterion with texts that belong to the option or level, replacing any set before.</summary>
    /// <param name="examples">The texts; <see langword="null"/> entries are left out of the wire.</param>
    /// <returns>A new criterion.</returns>
    /// <exception cref="InvalidOperationException">This criterion is JSON: put examples inside the JSON instead.</exception>
    public Criterion WithExamples(params ReadOnlySpan<string?> examples) => new(TextOnly(), default, examples.ToArray(), _notFor);

    /// <summary>Returns this text criterion with texts that do not belong to the option or level, replacing any set before.</summary>
    /// <param name="notFor">The texts; <see langword="null"/> entries are left out of the wire.</param>
    /// <returns>A new criterion.</returns>
    /// <exception cref="InvalidOperationException">This criterion is JSON: put not-for texts inside the JSON instead.</exception>
    public Criterion WithNotFor(params ReadOnlySpan<string?> notFor) => new(TextOnly(), default, _examples, notFor.ToArray());

    /// <summary>Converts a plain text to a criterion.</summary>
    /// <param name="description">What the option or level means.</param>
    /// <returns>The criterion.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is <see langword="null"/>.</exception>
    public static implicit operator Criterion(string description) => Text(description);

    // The non-null entries, read-only, computed once per criterion; no list is allocated when there are none.
    private static ReadOnlyCollection<string> Texts(string?[] entries)
    {
        var count = 0;
        foreach (var entry in entries)
        {
            if (entry is not null)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return ReadOnlyCollection<string>.Empty;
        }

        var texts = new string[count];
        var next = 0;
        foreach (var entry in entries)
        {
            if (entry is not null)
            {
                texts[next++] = entry;
            }
        }

        return new ReadOnlyCollection<string>(texts);
    }

    private string TextOnly()
        => _description ?? throw new InvalidOperationException(
            "Examples and not-for texts apply to a text criterion; put them inside the JSON instead.");
}
