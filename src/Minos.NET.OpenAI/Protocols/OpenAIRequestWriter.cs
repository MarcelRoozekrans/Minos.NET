using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using Minos.Transport;

namespace Minos.OpenAI.Protocols;

/// <summary>
/// Writes a <c>/v1/decisions</c> request body straight into a pooled <see cref="RawJson"/>:
/// <c>{"model":…,"input":…,"questions":…}</c>, with the protocol's cached <c>questions</c> bytes for a definition.
/// </summary>
/// <remarks>
/// <c>input</c> is the state as one string when there are no images, JSON state as its raw text. With images it is one
/// <c>user</c> message whose content is an <c>input_text</c> part holding that string, then one <c>input_image</c> part
/// per image, in order.
/// </remarks>
internal static class OpenAIRequestWriter
{
    /// <summary>
    /// The escaping of every OpenAI body: a quote as <c>\"</c>, and characters outside ASCII as themselves. The body is
    /// sent as <c>application/json</c>, never embedded in HTML, so the default encoder's HTML-safe escapes buy nothing.
    /// </summary>
    internal static readonly JsonWriterOptions Options = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // Room for the property names, the model and the braces around them, so a typical body needs no regrowth.
    private const int Overhead = 64;

    // The message around the parts and the text part's names: [{"role":"user","content":[{"type":"input_text","text":…}]}].
    private const int MessageOverhead = 72;

    // The braces, names and quotes around one image part: {"type":"input_image","image_url":…},.
    private const int ImageOverhead = 40;

    // Rents the body and writes {"model":…,"input":, then the state through writeState, then ,"questions":…}. The body is
    // disposed if anything throws, so the caller owns it only once it is returned.
    public static RawJson Write<TArg>(
        ReadOnlySpan<byte> questionsUtf8, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
        IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
        where TArg : allows ref struct
    {
        var imageBytes = images.Count == 0 ? 0 : MessageOverhead;
        for (var i = 0; i < images.Count; i++)
        {
            imageBytes += images[i].DataUrlLength + ImageOverhead;
        }

        var body = RawJson.Create(pool, questionsUtf8.Length + model.Length + stateSizeHint + imageBytes + Overhead);
        try
        {
            using (var writer = new Utf8JsonWriter(body, Options))
            {
                writer.WriteStartObject();
                writer.WriteString("model"u8, model);
                writer.WritePropertyName("input"u8);
                if (images.Count == 0)
                {
                    writeState(writer, body, state, asText: true);
                }
                else
                {
                    WriteMessage(writer, body, state, writeState, images);
                }

                writer.WritePropertyName("questions"u8);
                writer.WriteRawValue(questionsUtf8, skipInputValidation: true);
                writer.WriteEndObject();
                writer.Flush();
            }

            return body;
        }
        catch
        {
            body.Dispose();
            throw;
        }
    }

    // One user message: the state as an input_text part, then an input_image part per image.
    private static void WriteMessage<TArg>(
        Utf8JsonWriter writer, RawJson body, TArg state, StateWriter<TArg> writeState, IReadOnlyList<DecisionImage> images)
        where TArg : allows ref struct
    {
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("role"u8, "user"u8);
        writer.WriteStartArray("content"u8);
        writer.WriteStartObject();
        writer.WriteString("type"u8, "input_text"u8);
        writer.WritePropertyName("text"u8);
        writeState(writer, body, state, asText: true);
        writer.WriteEndObject();
        for (var i = 0; i < images.Count; i++)
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "input_image"u8);
            writer.WritePropertyName("image_url"u8);
            images[i].WriteDataUrl(writer);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteEndArray();
    }
}
