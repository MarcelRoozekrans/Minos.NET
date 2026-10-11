using System.Buffers;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Minos.OpenAI.Protocols;

/// <summary>
/// Writes the <c>questions</c> array of a <c>/v1/decisions</c> request for a definition, in definition order. OpenAI's
/// fields are strings, so JSON instructions and JSON criteria are sent as their raw JSON text.
/// </summary>
internal static class OpenAIQuestionsWriter
{
    /// <summary>Writes the <c>questions</c> array of <paramref name="definition"/>.</summary>
    public static byte[] Write(QuestionSetDefinition definition)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, OpenAIRequestWriter.Options))
        {
            writer.WriteStartArray();
            foreach (var question in definition.QuestionArray)
            {
                writer.WriteStartObject();
                writer.WriteString("type"u8, TypeName(question.Kind));
                writer.WriteString("name"u8, question.Key);
                writer.WritePropertyName("instructions"u8);
                WriteText(writer, question.Instructions);
                switch (question.Kind)
                {
                    case QuestionKind.Choice:
                        WriteChoices(writer, question);
                        break;
                    case QuestionKind.Score:
                        WriteLevels(writer, question);
                        break;
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static ReadOnlySpan<byte> TypeName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "predicate"u8,
        QuestionKind.Choice => "choice"u8,
        _ => "score"u8,
    };

    // Each option's value is its key; an option without a criterion has no description.
    private static void WriteChoices(Utf8JsonWriter writer, QuestionDefinition question)
    {
        writer.WriteStartArray("choices"u8);
        foreach (var option in question.OptionArray)
        {
            writer.WriteStartObject();
            writer.WriteString("value"u8, option.Key);
            if (option.Criterion is { } criterion)
            {
                writer.WritePropertyName("description"u8);
                WriteDescription(writer, criterion);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // Levels go lowest first, labelled by their index. Both Score Level methods require a criterion, so every level has one.
    private static void WriteLevels(Utf8JsonWriter writer, QuestionDefinition question)
    {
        writer.WriteStartArray("levels"u8);
        var options = question.OptionArray;
        for (var i = 0; i < options.Length; i++)
        {
            writer.WriteStartObject();
            writer.WriteString("label"u8, i.ToString(CultureInfo.InvariantCulture));
            writer.WritePropertyName("description"u8);
            WriteDescription(writer, options[i].Criterion!);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    // A criterion's text, or its JSON's raw text. Examples and NotFor are not sent: CollectUnsupported rejects them first.
    private static void WriteDescription(Utf8JsonWriter writer, Criterion criterion)
    {
        if (criterion.Description is { } description)
        {
            writer.WriteStringValue(description);
        }
        else
        {
            WriteText(writer, criterion.JsonContent!.Value);
        }
    }

    // Text as itself, JSON as its raw text.
    private static void WriteText(Utf8JsonWriter writer, DecisionContent content)
    {
        if (content.TryGetString(out var text))
        {
            writer.WriteStringValue(text);
        }
        else if (content.TryGetJson(out var json))
        {
            writer.WriteStringValue(JsonMarshal.GetRawUtf8Value(json));
        }
    }
}
