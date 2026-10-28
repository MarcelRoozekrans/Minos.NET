using Microsoft.Extensions.AI;

namespace Minos.Extensions.AI;

/// <summary>Turns an <see cref="IChatClient"/> into an <see cref="IDecisionClient"/>.</summary>
public static class ChatClientDecisionExtensions
{
    /// <summary>Creates a <see cref="ChatDecisionClient"/> that answers question sets over <paramref name="chatClient"/>.</summary>
    /// <param name="chatClient">The chat client to ask. It is not disposed with the decision client.</param>
    /// <param name="options">The configuration; <see langword="null"/> uses the defaults.</param>
    /// <returns>The decision client.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="chatClient"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An option has an invalid value.</exception>
    public static ChatDecisionClient AsDecisionClient(this IChatClient chatClient, ChatDecisionClientOptions? options = null)
        => new(chatClient, options);
}
