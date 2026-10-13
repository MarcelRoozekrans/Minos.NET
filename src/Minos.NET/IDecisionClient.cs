using ZeroAlloc.Results;

namespace Minos;

/// <summary>Asks a decision provider a question set's questions about a state. Protocol packages implement it, such as <c>SystemOneDecisionClient</c> and <c>OpenAIDecisionClient</c>; stages derive from <see cref="DelegatingDecisionClient"/>.</summary>
/// <remarks>
/// The typed <c>EvaluateAsync&lt;T&gt;</c> calls and the <see cref="QuestionSet"/> calls are extension methods in
/// <see cref="DecisionClientExtensions"/>, so they work over any implementation, a test fake included. A fake implements
/// <see cref="EvaluateAsync"/>, returning a <see cref="DecisionResponse"/> built with its public constructor,
/// <see cref="GetService"/> and <see cref="IDisposable.Dispose"/>.
/// </remarks>
public interface IDecisionClient : IDisposable
{
    /// <summary>Asks the request's questions about its state.</summary>
    /// <param name="request">The questions, the state and, optionally, the model.</param>
    /// <param name="cancellationToken">Cancels the call; cancellation throws <see cref="OperationCanceledException"/>.</param>
    /// <returns>The answers, or the <see cref="DecisionError"/> that prevented them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="ObjectDisposedException">The client was disposed before the call.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    ValueTask<Result<DecisionResponse, DecisionError>> EvaluateAsync(DecisionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns an object of <paramref name="serviceType"/> that this client or a client it wraps provides, such as <see cref="DecisionClientMetadata"/> or a stage.</summary>
    /// <param name="serviceType">The type to look for.</param>
    /// <param name="serviceKey">An optional key; built-in clients return nothing for a non-null key.</param>
    /// <returns>The object, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="serviceType"/> is <see langword="null"/>.</exception>
    object? GetService(Type serviceType, object? serviceKey = null);
}
