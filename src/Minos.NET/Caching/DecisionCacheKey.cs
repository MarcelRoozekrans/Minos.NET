using System.Buffers;
using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Minos;

/// <summary>
/// The cache key of a request, for <c>Minos.NET.Caching</c>: a SHA-256 digest over the definition, the state, the images,
/// the model, and the client's provider and endpoint. The retry attempt is left out, so a retried call finds the entry
/// its first attempt stored.
/// </summary>
/// <remarks>
/// Every variable-length field is written with its length first, so two different requests never write the same bytes.
/// The definition's part is a digest of its own, computed once per definition instance and kept in its protocol-data
/// slot. Each image is hashed on its own, so the buffer never holds image bytes.
/// </remarks>
internal static class DecisionCacheKey
{
    // Bumped when the layout below changes, so keys written by an older layout are never read as current ones.
    private const byte Layout = 1;

    private const int DefinitionSlot = 2;

    private const int DigestLength = 32;

    // The digest's unpadded Base64Url length.
    private const int EncodedLength = 43;

    public static string Create(DecisionRequest request, string provider, string? endpoint, string prefix)
    {
        var definition = request.Definition.GetOrAddProtocolData(DefinitionSlot, static d => DefinitionDigest(d));
        Span<byte> digest = stackalloc byte[DigestLength];
        Span<byte> imageDigest = stackalloc byte[DigestLength];
        var writer = new Writer(256);
        try
        {
            writer.Tag(Layout);
            writer.Bytes(definition);
            request.State.WriteCacheKey(ref writer);
            writer.Text(request.Model);
            writer.Text(provider);
            writer.Text(endpoint);
            var images = request.Images;
            writer.Int(images.Count);
            for (var i = 0; i < images.Count; i++)
            {
                writer.Text(images[i].MediaType);
                SHA256.HashData(images[i].Data.Span, imageDigest);
                writer.Bytes(imageDigest);
            }

            writer.Hash(digest);
        }
        finally
        {
            writer.Dispose();
        }

        return string.Create(prefix.Length + EncodedLength, (prefix, Digest: digest.ToArray()), static (chars, state) =>
        {
            state.prefix.AsSpan().CopyTo(chars);
            Base64Url.EncodeToChars(state.Digest, chars[state.prefix.Length..]);
        });
    }

    private static byte[] DefinitionDigest(QuestionSetDefinition definition)
    {
        var writer = new Writer(1024);
        try
        {
            var questions = definition.QuestionArray;
            writer.Int(questions.Length);
            foreach (var question in questions)
            {
                writer.Text(question.Key);
                writer.Int((int)question.Kind);
                question.Instructions.WriteCacheKey(ref writer);
                Optional(ref writer, question.WhenTrue);
                Optional(ref writer, question.WhenFalse);
                var options = question.OptionArray;
                writer.Int(options.Length);
                foreach (var option in options)
                {
                    writer.Text(option.Key);
                    Criterion(ref writer, option.Criterion);
                }
            }

            var digest = new byte[DigestLength];
            writer.Hash(digest);
            return digest;
        }
        finally
        {
            writer.Dispose();
        }
    }

    private static void Optional(ref Writer writer, DecisionContent? content)
    {
        if (content is { } value)
        {
            writer.Tag(1);
            value.WriteCacheKey(ref writer);
        }
        else
        {
            writer.Tag(0);
        }
    }

    private static void Criterion(ref Writer writer, Criterion? criterion)
    {
        if (criterion is null)
        {
            writer.Tag(0);
            return;
        }

        writer.Tag(1);
        writer.Text(criterion.Description);
        Optional(ref writer, criterion.JsonContent);
        Texts(ref writer, criterion.ExampleEntries);
        Texts(ref writer, criterion.NotForEntries);
    }

    private static void Texts(ref Writer writer, ReadOnlySpan<string?> texts)
    {
        writer.Int(texts.Length);
        foreach (var text in texts)
        {
            writer.Text(text);
        }
    }

    /// <summary>A growable byte buffer rented from the shared pool; dispose it to return the buffer.</summary>
    internal struct Writer
    {
        private byte[] _buffer;
        private int _length;

        public Writer(int capacity) => _buffer = ArrayPool<byte>.Shared.Rent(capacity);

        public void Tag(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void Int(int value)
        {
            Ensure(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += sizeof(int);
        }

        public void Bytes(ReadOnlySpan<byte> value)
        {
            Int(value.Length);
            Ensure(value.Length);
            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        // A null text is written as length -1, so it differs from an empty one.
        public void Text(string? value)
        {
            if (value is null)
            {
                Int(-1);
                return;
            }

            var count = Encoding.UTF8.GetByteCount(value);
            Int(count);
            Ensure(count);
            _length += Encoding.UTF8.GetBytes(value, _buffer.AsSpan(_length));
        }

        public readonly void Hash(Span<byte> destination) => SHA256.HashData(_buffer.AsSpan(0, _length), destination);

        public void Dispose()
        {
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = [];
        }

        private void Ensure(int count)
        {
            if (_length + count <= _buffer.Length)
            {
                return;
            }

            var larger = ArrayPool<byte>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + count));
            _buffer.AsSpan(0, _length).CopyTo(larger);
            ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = larger;
        }
    }
}
