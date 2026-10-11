using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Minos.SystemOne.Protocols;
using Minos.Transport;

namespace Minos.Tests;

/// <summary>The protocol writes the definition's questions around each kind of state, so a set built at run time can use it too.</summary>
public sealed class RequestStatesTests
{
    // The writer escapes a quote inside a string as a backslash, u0022.
    private static readonly string EscapedQuote = @"\" + "u0022";

    [Fact]
    public void Write_CopiesTheDefinitionsQuestions()
    {
        var pool = new CountingPool();
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("q", "x"));

        using (var body = SystemOneProtocol.Instance.WriteRequest(definition, "text", [], "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.Equal("""{"q":{"type":"noul","instructions":"x"}}""", sent["questions"]!.ToJsonString());
            Assert.Equal("text", (string?)sent["state"]);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void WriteJsonElement_CopiesTheDefinitionsQuestions()
    {
        var pool = new CountingPool();
        using var state = JsonDocument.Parse("""{"a":1}""");

        using (var body = SystemOneProtocol.Instance.WriteRequest(UrgencyCheck.Definition, state.RootElement, [], "m", pool))
        {
            var sent = JsonNode.Parse(body.Span)!;
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(SystemOneProtocol.QuestionsJson(UrgencyCheck.Definition)), sent["questions"]));
            Assert.Equal(1, (int)sent["state"]!["a"]!);
            Assert.Equal("m", (string?)sent["model"]);
        }

        Assert.Equal(0, pool.Outstanding);
    }

    [Fact]
    public void AsText_String_WritesTheText()
    {
        Assert.Equal("""
            "plain text"
            """, TextState("plain text"));
        Assert.Equal("\"a" + EscapedQuote + "b\"", TextState("a\"b"));
    }

    [Fact]
    public void AsText_JsonStringElement_WritesItsDecodedValue()
    {
        using var state = JsonDocument.Parse("""
            "a\/b"
            """);
        Assert.Equal("""
            "a/b"
            """, TextState(DecisionContent.FromJson(state.RootElement)));
    }

    [Fact]
    public void AsText_JsonObjectElement_WritesItsRawText()
    {
        using var state = JsonDocument.Parse("""{"a": 1}""");
        Assert.Equal("\"{" + EscapedQuote + "a" + EscapedQuote + ": 1}\"", TextState(DecisionContent.FromJson(state.RootElement)));
    }

    [Fact]
    public void AsText_Utf8UnescapedString_WritesItsValue()
        => Assert.Equal("""
            "hi"
            """, TextState(DecisionContent.FromUtf8Json("""
            "hi"
            """u8)));

    [Fact]
    public void AsText_Utf8EscapedString_WritesItsDecodedValue()
        => Assert.Equal("""
            "a/b"
            """, TextState(DecisionContent.FromUtf8Json("""
            "a\/b"
            """u8)));

    [Fact]
    public void AsText_Utf8Object_WritesTheRawJsonAsText()
        => Assert.Equal(
            "\"{" + EscapedQuote + "a" + EscapedQuote + ": [1]}\"",
            TextState(DecisionContent.FromUtf8Json("""{"a": [1]}"""u8)));

    // Writes only the state, through the state writer with asText set, and returns the bytes.
    private static string TextState(DecisionContent state)
    {
        var pool = new CountingPool();
        string written;
        using (var body = TextStateProtocol.Instance.WriteRequest(UrgencyCheck.Definition, state, [], "m", pool))
        {
            written = Encoding.UTF8.GetString(body.Span);
        }

        Assert.Equal(0, pool.Outstanding);
        return written;
    }

    // A protocol whose body is the state alone, written as text.
    private sealed class TextStateProtocol : IDecisionProtocol
    {
        public static TextStateProtocol Instance { get; } = new();

        public RawJson WriteRequest<TArg>(
            QuestionSetDefinition definition, TArg state, int stateSizeHint, StateWriter<TArg> writeState,
            IReadOnlyList<DecisionImage> images, string model, ArrayPool<byte> pool)
            where TArg : allows ref struct
        {
            var body = RawJson.Create(pool, stateSizeHint + 16);
            using (var writer = new Utf8JsonWriter(body))
            {
                writeState(writer, body, state, asText: true);
            }

            return body;
        }

        public void CollectUnsupported(QuestionSetDefinition definition, string providerName, ref List<QuestionFailure>? failures)
            => throw new NotSupportedException();

        public TResult ReadAnswers<TResult>(ref Utf8JsonReader answers, QuestionSetDefinition definition, AnswerFactory<TResult> create)
            => throw new NotSupportedException();

        public ZeroAlloc.Results.Result<DecisionResponse, DecisionError> ReadResponse(ReadOnlySpan<byte> body, QuestionSetDefinition definition)
            => throw new NotSupportedException();

        public DecisionError MapError(int statusCode, ReadOnlySpan<byte> body, bool bodyTruncated, string? contentType, TimeSpan? retryAfter, string? requestId)
            => throw new NotSupportedException();
    }
}
