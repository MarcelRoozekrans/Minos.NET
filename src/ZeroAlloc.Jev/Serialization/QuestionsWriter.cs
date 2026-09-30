using System.Buffers;
using System.Text.Json;
using ZeroAlloc.Jev.Validation;

namespace ZeroAlloc.Jev.Serialization;

/// <summary>
/// Writes a built set's <c>questions</c> object in the generator's layout and escaping, so a set built at run time
/// sends the same bytes as the equivalent <c>[JevQuestions]</c> set.
/// </summary>
internal static class QuestionsWriter
{
    private static readonly JsonWriterOptions Options = new() { Encoder = GeneratorJsonEncoder.Instance };

    /// <summary>Writes <paramref name="questions"/>, which have passed validation.</summary>
    public static byte[] Write(QuestionSpec[] questions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            foreach (var question in questions)
            {
                writer.WritePropertyName(question.Key);
                writer.WriteStartObject();
                writer.WriteString("type"u8, TypeName(question.Kind));
                writer.WritePropertyName("instructions"u8);
                WriteContent(writer, question.Instructions);
                WriteCriteria(writer, question);
                writer.WriteEndObject();
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static ReadOnlySpan<byte> TypeName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "noul"u8,
        QuestionKind.Choice => "choice"u8,
        _ => "score"u8,
    };

    private static void WriteCriteria(Utf8JsonWriter writer, QuestionSpec question)
    {
        switch (question.Kind)
        {
            case QuestionKind.Noul when question.WhenTrue is not null || question.WhenFalse is not null:
                writer.WriteStartObject("criteria"u8);
                if (question.WhenTrue is { } yes)
                {
                    writer.WritePropertyName("true"u8);
                    WriteContent(writer, yes);
                }

                if (question.WhenFalse is { } no)
                {
                    writer.WritePropertyName("false"u8);
                    WriteContent(writer, no);
                }

                writer.WriteEndObject();
                break;

            case QuestionKind.Choice:
                writer.WriteStartObject("criteria"u8);
                foreach (var option in question.Options)
                {
                    writer.WritePropertyName(option.Key);
                    if (option.Criterion is null)
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        option.Criterion.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
                break;

            case QuestionKind.Score:
                writer.WriteStartArray("criteria"u8);
                foreach (var option in question.Options)
                {
                    // Both Score Level methods require a criterion, so every level has one, in the order it was given.
                    option.Criterion!.WriteTo(writer);
                }

                writer.WriteEndArray();
                break;
        }
    }

    private static void WriteContent(Utf8JsonWriter writer, JevContent content)
    {
        if (content.TryGetString(out var text))
        {
            writer.WriteStringValue(text);
        }
        else if (content.TryGetJson(out var json))
        {
            json.WriteTo(writer);
        }
    }
}
