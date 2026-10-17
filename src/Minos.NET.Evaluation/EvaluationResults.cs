using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>The raw outcome of running a dataset: a header and one result per item, in dataset order.</summary>
public sealed class EvaluationResults
{
    /// <summary>Creates results.</summary>
    /// <param name="header">What the run was.</param>
    /// <param name="items">The item results, in dataset order.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public EvaluationResults(RunHeader header, IReadOnlyList<ItemResult> items)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(items);
        Header = header;
        Items = items;
    }

    /// <summary>Gets what the run was.</summary>
    public RunHeader Header { get; }

    /// <summary>Gets the item results, in dataset order.</summary>
    public IReadOnlyList<ItemResult> Items { get; }

    /// <summary>Saves the results to a file as versioned UTF-8 JSONL, replacing the file when it exists.</summary>
    /// <param name="path">The file to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public void Save(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(stream);
    }

    /// <summary>Saves the results to a stream as versioned UTF-8 JSONL, with LF line endings and no byte order mark. The stream stays open.</summary>
    /// <param name="utf8Jsonl">The stream to write.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Jsonl"/> is <see langword="null"/>.</exception>
    public void Save(Stream utf8Jsonl)
    {
        ArgumentNullException.ThrowIfNull(utf8Jsonl);
        ResultsWriter.Write(this, utf8Jsonl);
    }

    /// <summary>Loads results from a file written by <see cref="Save(string)"/>.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The results, or every line-numbered problem found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    public static Result<EvaluationResults, DatasetProblems> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream);
    }

    /// <summary>Parses results from UTF-8 JSONL written by <see cref="Save(Stream)"/>.</summary>
    /// <param name="utf8Jsonl">The stream to read.</param>
    /// <returns>The results, or every line-numbered problem found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Jsonl"/> is <see langword="null"/>.</exception>
    public static Result<EvaluationResults, DatasetProblems> Parse(Stream utf8Jsonl)
    {
        ArgumentNullException.ThrowIfNull(utf8Jsonl);
        return ResultsReader.Read(new Utf8LineReader(utf8Jsonl));
    }
}
