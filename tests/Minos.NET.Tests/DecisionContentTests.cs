using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Minos.Serialization;

namespace Minos.Tests;

public sealed record ContentSample(string Name, int Age);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ContentSample))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int[]))]
internal sealed partial class ContentJsonContext : JsonSerializerContext;

public sealed class DecisionContentTests
{
    [Fact]
    public void ImplicitString_IsText()
    {
        DecisionContent content = "Does this convey urgency?";

        Assert.True(content.IsString);
        Assert.True(content.TryGetString(out var text));
        Assert.Equal("Does this convey urgency?", text);
        Assert.False(content.TryGetJson(out _));
    }

    [Fact]
    public void FromString_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => DecisionContent.FromString(null!));
    }

    [Fact]
    public void FromJson_Object_KeepsJson()
    {
        var content = DecisionContent.FromJson(Json("""{"name":"John Smith"}"""));

        Assert.False(content.IsString);
        Assert.True(content.TryGetJson(out var json));
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.Equal("John Smith", json.GetProperty("name").GetString());
    }

    [Fact]
    public void FromJson_StringElement_BecomesText()
    {
        var content = DecisionContent.FromJson(Json("\"Calm\""));

        Assert.True(content.TryGetString(out var text));
        Assert.Equal("Calm", text);
    }

    [Fact]
    public void FromJson_Null_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecisionContent.FromJson(Json("null")));
    }

    [Fact]
    public void FromJson_Undefined_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecisionContent.FromJson(default));
    }

    [Fact]
    public void FromJson_Number_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecisionContent.FromJson(Json("42")));
    }

    [Fact]
    public void FromJson_True_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecisionContent.FromJson(Json("true")));
    }

    [Fact]
    public void FromJson_False_Throws()
    {
        Assert.Throws<ArgumentException>(() => DecisionContent.FromJson(Json("false")));
    }

    [Fact]
    public void Equality_Text_IsOrdinal()
    {
        Assert.Equal(DecisionContent.FromString("a"), (DecisionContent)"a");
        Assert.NotEqual(DecisionContent.FromString("a"), (DecisionContent)"A");
        Assert.True((DecisionContent)"a" == "a");
        Assert.True((DecisionContent)"a" != "b");
    }

    [Fact]
    public void Equality_Json_IsDeepAndOrderInsensitive()
    {
        var left = DecisionContent.FromJson(Json("""{"a":1,"b":[1,2]}"""));
        var right = DecisionContent.FromJson(Json("""{"b":[1,2],"a":1}"""));

        Assert.Equal(left, right);
        Assert.Equal(left.GetHashCode(), right.GetHashCode());
        Assert.NotEqual(left, DecisionContent.FromJson(Json("""{"a":2,"b":[1,2]}""")));
    }

    [Fact]
    public void Equality_TextNeverEqualsJson()
    {
        Assert.NotEqual((DecisionContent)"[1]", DecisionContent.FromJson(Json("[1]")));
    }

    [Fact]
    public void ToString_ReturnsTextOrRawJson()
    {
        Assert.Equal("hi", ((DecisionContent)"hi").ToString());
        Assert.Equal("[1,2]", DecisionContent.FromJson(Json("[1,2]")).ToString());
        Assert.Equal(string.Empty, default(DecisionContent).ToString());
    }

    [Fact]
    public void Write_Text_WritesJsonString()
    {
        Assert.Equal("\"hi\"", Write("hi"));
    }

    [Fact]
    public void Write_Object_WritesRawJson()
    {
        Assert.Equal("""{"a":[1,2]}""", Write(DecisionContent.FromJson(Json("""{"a":[1,2]}"""))));
    }

    [Fact]
    public void Write_Default_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Write(default));
    }

    [Fact]
    public void Read_String_IsText()
    {
        Assert.Equal((DecisionContent)"Calm", Read("\"Calm\""));
    }

    [Fact]
    public void Read_Array_IsJson()
    {
        var content = Read("""["Calm","Frustrated"]""");

        Assert.True(content.TryGetJson(out var json));
        Assert.Equal(JsonValueKind.Array, json.ValueKind);
        Assert.Equal(2, json.GetArrayLength());
    }

    [Fact]
    public void Read_Null_Throws()
    {
        Assert.Throws<JsonException>(() => Read("null"));
    }

    [Fact]
    public void Read_Number_Throws()
    {
        Assert.Throws<JsonException>(() => Read("42"));
    }

    [Fact]
    public void Read_True_Throws()
    {
        Assert.Throws<JsonException>(() => Read("true"));
    }

    [Fact]
    public void FromValue_Object_IsJson()
    {
        var content = DecisionContent.FromValue(new ContentSample("John", 42), ContentJsonContext.Default.ContentSample);

        Assert.True(content.TryGetJson(out var json));
        Assert.Equal("John", json.GetProperty("name").GetString());
    }

    [Fact]
    public void FromValue_Array_IsJson()
        => Assert.True(DecisionContent.FromValue([1, 2], ContentJsonContext.Default.Int32Array).TryGetJson(out _));

    [Fact]
    public void FromValue_String_IsText()
    {
        var content = DecisionContent.FromValue("hello", ContentJsonContext.Default.String);

        Assert.True(content.TryGetString(out var text));
        Assert.Equal("hello", text);
    }

    [Fact]
    public void FromValue_Number_Throws()
        => Assert.Equal("value", Assert.Throws<ArgumentException>(() => DecisionContent.FromValue(5, ContentJsonContext.Default.Int32)).ParamName);

    [Fact]
    public void FromValue_NullTypeInfo_Throws()
        => Assert.Throws<ArgumentNullException>(() => DecisionContent.FromValue<int>(5, null!));

    [Theory]
    [InlineData("{\"a\":1}", false)]
    [InlineData("[1,2]", false)]
    [InlineData("\"text\"", true)]
    public void FromUtf8Json_Accepted(string json, bool isString)
        => Assert.Equal(isString, DecisionContent.FromUtf8Json(Encoding.UTF8.GetBytes(json)).IsString);

    [Theory]
    [InlineData("")]
    [InlineData("5")]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("{} {}")]
    [InlineData("{")]
    public void FromUtf8Json_Rejected(string json)
        => Assert.Equal(
            "utf8Json",
            Assert.Throws<ArgumentException>(() => DecisionContent.FromUtf8Json(Encoding.UTF8.GetBytes(json))).ParamName);

    [Fact]
    public void FromUtf8Json_WithBom_IsRejected_AsTheStatePathIs()
    {
        byte[] withBom = [0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}'];

        Assert.Throws<ArgumentException>(() => DecisionContent.FromUtf8Json(withBom));
    }

    [Fact]
    public void FromUtf8Json_DoesNotReferenceInput()
    {
        var bytes = Encoding.UTF8.GetBytes("{\"a\":\"b\"}");
        var content = DecisionContent.FromUtf8Json(bytes);
        Array.Clear(bytes);

        Assert.True(content.TryGetJson(out var json));
        Assert.Equal("b", json.GetProperty("a").GetString());
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    [Theory]
    [InlineData("{")]
    [InlineData("")]
    [InlineData("1 2")]
    [InlineData("[1]]")]
    public void EnsureSingleJsonValue_RejectsAnythingButOneValue(string json)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => DecisionContent.EnsureSingleJsonValue(Encoding.UTF8.GetBytes(json), "arg"));

        Assert.Equal("arg", exception.ParamName);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(" [1, {\"a\": null}] ")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    public void EnsureSingleJsonValue_AcceptsOneValue(string json)
        => DecisionContent.EnsureSingleJsonValue(Encoding.UTF8.GetBytes(json), "arg");

    private static string Write(DecisionContent value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            new DecisionContentConverter().Write(writer, value, JsonSerializerOptions.Default);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static DecisionContent Read(string json)
    {
        var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(json));
        reader.Read();
        return new DecisionContentConverter().Read(ref reader, typeof(DecisionContent), JsonSerializerOptions.Default);
    }
}
