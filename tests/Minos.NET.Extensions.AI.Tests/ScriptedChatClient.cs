using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI.Tests;

/// <summary>
/// A fake <see cref="IChatClient"/> that keeps the messages and options of every call and answers each one from the
/// function it was given. Thread-safe, so parallel calls can share it.
/// </summary>
internal sealed class ScriptedChatClient : IChatClient
{
    private readonly Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> _respond;
    private readonly ChatClientMetadata? _metadata;
    private readonly Lock _gate = new();
    private readonly List<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> _calls = [];

    public ScriptedChatClient(Func<IEnumerable<ChatMessage>, ChatOptions?, ChatResponse> respond, ChatClientMetadata? metadata = null)
    {
        _respond = respond;
        _metadata = metadata;
    }

    /// <summary>Gets every call so far, in the order they arrived.</summary>
    public IReadOnlyList<(IReadOnlyList<ChatMessage> Messages, ChatOptions? Options)> Calls
    {
        get
        {
            lock (_gate)
            {
                return [.. _calls];
            }
        }
    }

    public bool IsDisposed { get; private set; }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var list = messages.ToList();
        lock (_gate)
        {
            _calls.Add((list, options));
        }

        return Task.FromResult(_respond(list, options));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken).ConfigureAwait(false);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        if (serviceKey is not null)
        {
            return null;
        }

        if (serviceType == typeof(ChatClientMetadata))
        {
            return _metadata;
        }

        return serviceType.IsInstanceOfType(this) ? this : null;
    }

    public void Dispose() => IsDisposed = true;
}
