using System.Buffers;
using System.Text.Json;
using Minos.Transport;

namespace Minos.SystemOne.Protocols;

/// <summary>
/// Writes a <c>/v1/systemone</c> request body straight into a pooled <see cref="RawJson"/>: the state, the model, the
/// protocol's cached <c>questions</c> bytes for a definition and the client's <c>provider</c> member, without building a
/// <see cref="SystemOneRequest"/>. The body
/// is JSON-equal to a <see cref="SystemOneRequest"/> with the same questions, state and model, serialized.
/// </summary>
internal static class SystemOneRequestWriter
{
    // Room for the property names, the model and the braces around them, so a typical body needs no regrowth.
    private const int Overhead = 64;

    // The braces, names and quotes around one image part: {"type":"image_url","image_url":{"url":…}},.
    private const int ImageOverhead = 48;

    // Rents the body and writes {"state":, then the state through writeState, then ,"model":…,"questions":…, then the
    // providerMember bytes as they are, ,"provider":{…} or nothing, then }. The body is disposed if anything throws, so
    // the caller owns it only once it is returned.
    public static RawJson Write<TArg>(
        ReadOnlySpan<byte> questionsUtf8, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
        IReadOnlyList<DecisionImage> images, string model, ReadOnlySpan<byte> providerMember, ArrayPool<byte> pool)
        where TArg : allows ref struct
    {
        var imageBytes = 0;
        for (var i = 0; i < images.Count; i++)
        {
            imageBytes += images[i].DataUrlLength + ImageOverhead;
        }

        var body = RawJson.Create(pool, questionsUtf8.Length + model.Length + stateSizeHint + imageBytes + providerMember.Length + Overhead);
        try
        {
            using (var writer = new Utf8JsonWriter(body))
            {
                writer.WriteStartObject();
                writer.WritePropertyName("state"u8);
                if (images.Count == 0)
                {
                    writeState(writer, body, state, asText: false);
                }
                else
                {
                    WriteParts(writer, body, state, writeState, images);
                }

                writer.WriteString("model"u8, model);
                writer.WritePropertyName("questions"u8);
                writer.WriteRawValue(questionsUtf8, skipInputValidation: true);
                writer.Flush();
            }

            // Utf8JsonWriter cannot append a pre-written member, so the provider bytes and the closing brace go straight
            // into the body after the writer has flushed everything before them.
            body.Write(providerMember);
            body.Write("}"u8);

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    // With images the state becomes content parts: a text part holding the state, then one image_url part per image.
    private static void WriteParts<TArg>(
        Utf8JsonWriter writer, RawJson body, TArg state, StateWriter<TArg> writeState, IReadOnlyList<DecisionImage> images)
        where TArg : allows ref struct
    {
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("type"u8, "text"u8);
        writer.WritePropertyName("text"u8);
        writeState(writer, body, state, asText: true);
        writer.WriteEndObject();
        for (var i = 0; i < images.Count; i++)
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "image_url"u8);
            writer.WriteStartObject("image_url"u8);
            writer.WritePropertyName("url"u8);
            images[i].WriteDataUrl(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }
}
