using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics.CodeAnalysis;

namespace Minos.Extensions.AI.Tests;

/// <summary>An error response with a status and headers and no body, as the OpenAI SDK puts in a <see cref="ClientResultException"/>.</summary>
internal sealed class FakePipelineResponse(int status, params (string Name, string Value)[] headers) : PipelineResponse
{
    private readonly ResponseHeaders _headers = new(headers);

    /// <summary>A <see cref="ClientResultException"/> for this response, whose own message is <paramref name="message"/>.</summary>
    public static ClientResultException Exception(int status, string message = "The provider failed.", params (string Name, string Value)[] headers)
        => new(message, new FakePipelineResponse(status, headers));

    public override int Status => status;

    public override string ReasonPhrase => string.Empty;

    public override Stream? ContentStream { get; set; }

    public override BinaryData Content => BinaryData.Empty;

    protected override PipelineResponseHeaders HeadersCore => _headers;

    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => BinaryData.Empty;

    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(BinaryData.Empty);

    public override void Dispose()
    {
    }

    private sealed class ResponseHeaders((string Name, string Value)[] headers) : PipelineResponseHeaders
    {
        public override bool TryGetValue(string name, [NotNullWhen(true)] out string? value)
        {
            foreach (var header in headers)
            {
                if (string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = header.Value;
                    return true;
                }
            }

            value = null;
            return false;
        }

        public override bool TryGetValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values)
        {
            values = TryGetValue(name, out var value) ? [value] : null;
            return values is not null;
        }

        public override IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => headers.Select(header => new KeyValuePair<string, string>(header.Name, header.Value)).GetEnumerator();
    }
}
