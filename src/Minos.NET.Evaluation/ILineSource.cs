namespace Minos.Evaluation;

/// <summary>The lines of a dataset file, one at a time.</summary>
internal interface ILineSource
{
    /// <summary>Reads the next line.</summary>
    /// <param name="line">The line without its line break, or <see langword="null"/> when its bytes are not valid UTF-8.</param>
    /// <returns><see langword="false"/> at the end of the file.</returns>
    bool TryReadLine(out string? line);
}
