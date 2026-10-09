using System.Text.Json;

namespace Minos;

/// <summary>
/// Describes a Choice option or a Score level of a question built with <see cref="QuestionSetBuilder"/>: a plain
/// text, a text with <see cref="WithExamples"/> and <see cref="WithNotFor"/> texts, or JSON.
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
    }

    internal bool IsJson => _description is null;

    internal string? Description => _description;

    internal DecisionContent JsonContent => _json;

    internal ReadOnlySpan<string?> Examples => _examples;

    internal ReadOnlySpan<string?> NotFor => _notFor;

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

    /// <summary>Writes the criterion's wire fragment.</summary>
    internal void WriteTo(Utf8JsonWriter writer)
    {
        if (_description is null)
        {
            _json.TryGetJson(out var json);
            json.WriteTo(writer);
            return;
        }

        if (!HasAny(_examples) && !HasAny(_notFor))
        {
            writer.WriteStringValue(_description);
            return;
        }

        writer.WriteStartObject();
        writer.WriteString("description"u8, _description);
        WriteList(writer, "examples"u8, _examples);
        WriteList(writer, "not_for"u8, _notFor);
        writer.WriteEndObject();
    }

    private static bool HasAny(string?[] values)
    {
        foreach (var value in values)
        {
            if (value is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static void WriteList(Utf8JsonWriter writer, ReadOnlySpan<byte> name, string?[] values)
    {
        if (!HasAny(values))
        {
            return;
        }

        writer.WriteStartArray(name);
        foreach (var value in values)
        {
            if (value is not null)
            {
                writer.WriteStringValue(value);
            }
        }

        writer.WriteEndArray();
    }

    private string TextOnly()
        => _description ?? throw new InvalidOperationException(
            "Examples and not-for texts apply to a text criterion; put them inside the JSON instead.");
}
