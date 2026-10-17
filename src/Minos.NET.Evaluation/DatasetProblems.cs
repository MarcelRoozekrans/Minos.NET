using System.Collections;

namespace Minos.Evaluation;

/// <summary>Every problem found in a dataset file, in file order.</summary>
public sealed class DatasetProblems : IReadOnlyList<DatasetProblem>
{
    private readonly DatasetProblem[] _problems;

    internal DatasetProblems(DatasetProblem[] problems) => _problems = problems;

    /// <inheritdoc />
    public int Count => _problems.Length;

    /// <inheritdoc />
    public DatasetProblem this[int index] => _problems[index];

    /// <inheritdoc />
    public IEnumerator<DatasetProblem> GetEnumerator() => ((IEnumerable<DatasetProblem>)_problems).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
