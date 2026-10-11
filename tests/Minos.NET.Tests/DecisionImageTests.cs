namespace Minos.Tests;

public sealed class DecisionImageTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47];

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    [InlineData("image/gif")]
    public void FromBytes_AcceptsTheSupportedTypes(string mediaType)
    {
        var image = DecisionImage.FromBytes(Png, mediaType);

        Assert.Equal(mediaType, image.MediaType);
        Assert.Equal(Png, image.Data.ToArray());
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/plain")]
    [InlineData("IMAGE/PNG")]
    [InlineData("")]
    public void FromBytes_RejectsOtherTypes(string mediaType)
        => Assert.Throws<ArgumentException>(nameof(mediaType), () => DecisionImage.FromBytes(Png, mediaType));

    [Fact]
    public void FromBytes_RejectsEmptyData()
        => Assert.Throws<ArgumentException>("data", () => DecisionImage.FromBytes(ReadOnlyMemory<byte>.Empty, "image/png"));

    [Fact]
    public void FromBytes_RejectsNullMediaType()
        => Assert.Throws<ArgumentNullException>("mediaType", () => DecisionImage.FromBytes(Png, null!));

    [Fact]
    public void FromBytes_CopiesTheData()
    {
        var bytes = (byte[])Png.Clone();
        var image = DecisionImage.FromBytes(bytes, "image/png");

        bytes[0] = 0;

        Assert.Equal(0x89, image.Data.Span[0]);
    }

    [Fact]
    public void FromDataUrl_ReadsTypeAndData()
    {
        var image = DecisionImage.FromDataUrl("data:image/png;base64," + Convert.ToBase64String(Png));

        Assert.Equal("image/png", image.MediaType);
        Assert.Equal(Png, image.Data.ToArray());
    }

    [Theory]
    [InlineData("https://example.com/a.png")]
    [InlineData("data:image/png,raw")]
    [InlineData("data:image/svg+xml;base64,AAAA")]
    [InlineData("data:image/png;base64,***")]
    [InlineData("data:image/png;base64,")]
    public void FromDataUrl_RejectsAnythingElse(string dataUrl)
        => Assert.Throws<ArgumentException>(nameof(dataUrl), () => DecisionImage.FromDataUrl(dataUrl));

    [Fact]
    public void FromDataUrl_RejectsNull()
        => Assert.Throws<ArgumentNullException>("dataUrl", () => DecisionImage.FromDataUrl(null!));

    [Fact]
    public void Equality_ComparesTypeAndBytes()
    {
        var image = DecisionImage.FromBytes(Png, "image/png");

        Assert.True(image == DecisionImage.FromBytes((byte[])Png.Clone(), "image/png"));
        Assert.True(image.Equals((object)DecisionImage.FromBytes(Png, "image/png")));
        Assert.Equal(image.GetHashCode(), DecisionImage.FromBytes(Png, "image/png").GetHashCode());
        Assert.True(image != DecisionImage.FromBytes(Png, "image/gif"));
        Assert.True(image != DecisionImage.FromBytes(new byte[] { 1, 2, 3, 4 }, "image/png"));
        Assert.False(image.Equals(new object()));
        Assert.True(default(DecisionImage) == default);
    }

    [Fact]
    public void WriteDataUrl_WritesOneJsonString()
    {
        var image = DecisionImage.FromBytes(Png, "image/png");
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            image.WriteDataUrl(writer);
        }

        Assert.Equal("\"data:image/png;base64," + Convert.ToBase64String(Png) + "\"", System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.Equal(("data:image/png;base64," + Convert.ToBase64String(Png)).Length, image.DataUrlLength);
    }

    [Fact]
    public void WriteDataUrl_WritesPlusAndSlashUnescaped()
    {
        var image = DecisionImage.FromBytes(new byte[] { 0xFB, 0xFF }, "image/webp");
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            image.WriteDataUrl(writer);
            image.WriteDataUrl(writer);
            writer.WriteEndArray();
        }

        Assert.Equal("[\"data:image/webp;base64,+/8=\",\"data:image/webp;base64,+/8=\"]", System.Text.Encoding.UTF8.GetString(buffer.WrittenSpan));
        Assert.Equal("data:image/webp;base64,+/8=".Length, image.DataUrlLength);
    }

    // MediaType is declared non-null, so the default value reports an empty string rather than null.
    [Fact]
    public void Default_HasAnEmptyMediaTypeAndNoData()
    {
        var image = default(DecisionImage);

        Assert.Equal(string.Empty, image.MediaType);
        Assert.True(image.Data.IsEmpty);
        Assert.False(image.IsInitialized);
        Assert.Equal(default(DecisionImage).GetHashCode(), image.GetHashCode());
    }

    [Fact]
    public void Default_IsRejectedByTheRequest()
        => Assert.Throws<ArgumentException>("value", () => new DecisionRequest(QuestionSets.UrgencyDefinition(), DecisionContent.FromString("s")) { Images = [default] });
}
