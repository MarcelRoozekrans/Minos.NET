namespace Minos.Recording;

/// <summary>
/// The ambient recording scope: the decision id and erasure key that the recording stage puts on the record of the
/// decision made inside it. It flows with the async flow, as an <see cref="AsyncLocal{T}"/> does, so parallel flows each
/// see their own.
/// </summary>
/// <example>
/// <code>
/// using (var scope = DecisionRecording.Begin(erasureKey: customerId))
/// {
///     var answer = await client.EvaluateAsync(request);
///     // scope.DecisionId is the generated id, for outcomes and erasures later.
/// }
/// </code>
/// </example>
public static class DecisionRecording
{
    private static readonly AsyncLocal<ScopeState?> Ambient = new();

    /// <summary>Gets the scope active in this flow, or <see langword="null"/>.</summary>
    internal static ScopeState? Current => Ambient.Value is { IsDisposed: false } state ? state : null;

    /// <summary>
    /// Begins a scope for one decision. A scope holds one decision: a second request in it with
    /// <see cref="DecisionRequest.RetryAttempt"/> 0 throws <see cref="InvalidOperationException"/> before the inner call. A
    /// retry, with a higher attempt, is recorded under the same id. A call whose inner client throws makes no decision,
    /// so the scope can be used again.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Begin"/> in the method that awaits the decision call, or in one that calls that method. A scope
    /// begun inside an async helper does not flow back to the helper's caller: the caller's call is then made outside it,
    /// and recorded with an id of its own.
    /// </remarks>
    /// <param name="decisionId">The id for the record, or <see langword="null"/> to have the stage generate a UUIDv7 when the call is made.</param>
    /// <param name="erasureKey">The key that erases every record of one subject, or <see langword="null"/> for none.</param>
    /// <returns>The scope. Dispose it when the decision is made.</returns>
    /// <exception cref="ArgumentException"><paramref name="decisionId"/> or <paramref name="erasureKey"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">A scope is already active in this flow.</exception>
    public static DecisionRecordingScope Begin(string? decisionId = null, string? erasureKey = null)
    {
        if (decisionId is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(decisionId);
        }

        if (erasureKey is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(erasureKey);
        }

        if (Current is not null)
        {
            throw new InvalidOperationException(
                "A decision recording scope is already active in this flow: dispose it before beginning another. Scopes do not nest.");
        }

        var state = new ScopeState(decisionId, erasureKey);
        Ambient.Value = state;
        return new DecisionRecordingScope(state, Ambient);
    }

    /// <summary>The state of one scope, shared by the scope object and the stage.</summary>
    internal sealed class ScopeState(string? decisionId, string? erasureKey)
    {
        private string? _decisionId = decisionId;
        private int _hasDecision;
        private int _disposed;

        public string? DecisionId => Volatile.Read(ref _decisionId);

        public string? ErasureKey { get; } = erasureKey;

        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        /// <summary>Claims the scope's one fresh decision.</summary>
        /// <returns><see langword="false"/> when a decision was already made in the scope.</returns>
        public bool TryClaimFresh() => Interlocked.Exchange(ref _hasDecision, 1) == 0;

        /// <summary>Notes that a decision was made in the scope, for a retry that arrives first.</summary>
        public void MarkDecided() => Volatile.Write(ref _hasDecision, 1);

        /// <summary>Gets the scope's id, generating a UUIDv7 the first time when none was given.</summary>
        /// <param name="generated">Set when this call generated the id.</param>
        public string EnsureDecisionId(out bool generated)
        {
            generated = false;
            var id = Volatile.Read(ref _decisionId);
            if (id is not null)
            {
                return id;
            }

            var created = Guid.CreateVersion7().ToString();
            var existing = Interlocked.CompareExchange(ref _decisionId, created, null);
            generated = existing is null;
            return existing ?? created;
        }

        /// <summary>Gives back the fresh claim of a call whose inner client threw, and the id it generated, if any.</summary>
        /// <param name="generatedId">The id the call generated, or <see langword="null"/> when it was given or already set.</param>
        public void Release(string? generatedId)
        {
            if (generatedId is not null)
            {
                Interlocked.CompareExchange(ref _decisionId, null, generatedId);
            }

            Volatile.Write(ref _hasDecision, 0);
        }

        public void Dispose() => Volatile.Write(ref _disposed, 1);
    }
}
