using System.Collections;
using ZeroAlloc.Results;

namespace Minos.Evaluation;

/// <summary>
/// A labelled dataset: an immutable list of items read from a JSONL file, one item per line. The format is described
/// in the Labelled datasets page of the docs.
/// </summary>
public sealed class DecisionDataset : IReadOnlyList<DatasetItem>
{
    private readonly DatasetItem[] _items;

    internal DecisionDataset(DatasetItem[] items) => _items = items;

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public DatasetItem this[int index] => _items[index];

    /// <summary>Reads a dataset from JSONL text.</summary>
    /// <param name="jsonl">The file's content.</param>
    /// <returns>The dataset, or every problem found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="jsonl"/> is <see langword="null"/>.</exception>
    public static Result<DecisionDataset, DatasetProblems> Parse(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        using var reader = new StringReader(jsonl);
        return DatasetReader.Read(reader);
    }

    /// <summary>Reads a dataset from a UTF-8 JSONL stream, line by line.</summary>
    /// <param name="utf8Jsonl">The file's content. A byte order mark is skipped.</param>
    /// <returns>The dataset, or every problem found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Jsonl"/> is <see langword="null"/>.</exception>
    /// <exception cref="IOException">The stream cannot be read.</exception>
    public static Result<DecisionDataset, DatasetProblems> Parse(Stream utf8Jsonl)
    {
        ArgumentNullException.ThrowIfNull(utf8Jsonl);
        using var reader = new StreamReader(utf8Jsonl, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        return DatasetReader.Read(reader);
    }

    /// <summary>Reads a dataset from a UTF-8 JSONL file, line by line.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The dataset, or every problem found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    /// <exception cref="IOException">The file cannot be read.</exception>
    public static Result<DecisionDataset, DatasetProblems> Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Parse(stream);
    }

    /// <inheritdoc />
    public IEnumerator<DatasetItem> GetEnumerator() => ((IEnumerable<DatasetItem>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
