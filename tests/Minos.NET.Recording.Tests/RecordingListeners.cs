namespace Minos.Recording.Tests;

/// <summary>
/// The collection every test class joins when it creates a recorder or attaches a listener: the recording metrics and
/// the queue length gauge are process-wide, so this collection runs alone.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class RecordingListeners
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Recording listeners";
}
