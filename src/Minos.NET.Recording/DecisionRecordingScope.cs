namespace Minos.Recording;

/// <summary>
/// A recording scope, begun with <see cref="DecisionRecording.Begin"/>: the decision made inside it is recorded with its
/// id and erasure key. Dispose it when the decision is made.
/// </summary>
public sealed class DecisionRecordingScope : IDisposable
{
    private readonly DecisionRecording.ScopeState _state;
    private readonly AsyncLocal<DecisionRecording.ScopeState?> _ambient;

    internal DecisionRecordingScope(DecisionRecording.ScopeState state, AsyncLocal<DecisionRecording.ScopeState?> ambient)
    {
        _state = state;
        _ambient = ambient;
    }

    /// <summary>
    /// Gets the decision id: the one given to <see cref="DecisionRecording.Begin"/>, or the one the recording stage
    /// generated. A generated id is <see langword="null"/> until the decision is made.
    /// </summary>
    public string? DecisionId => _state.DecisionId;

    /// <summary>
    /// Ends the scope. A flow that still carries it, such as a task started inside it, no longer records under it, and a
    /// new scope can begin.
    /// </summary>
    public void Dispose()
    {
        _state.Dispose();
        if (ReferenceEquals(_ambient.Value, _state))
        {
            _ambient.Value = null;
        }
    }
}
