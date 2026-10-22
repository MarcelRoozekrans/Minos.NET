using System.Collections;

namespace Minos.Recording;

/// <summary>The problems found in a record file, in line order. An empty list means the file was read in full.</summary>
public sealed class DecisionRecordFileProblems : IReadOnlyList<DecisionRecordFileProblem>
{
    private readonly DecisionRecordFileProblem[] _problems;

    internal DecisionRecordFileProblems(DecisionRecordFileProblem[] problems) => _problems = problems;

    /// <summary>Gets the number of problems.</summary>
    public int Count => _problems.Length;

    /// <summary>Gets the problem at <paramref name="index"/>.</summary>
    /// <param name="index">The index, from 0.</param>
    public DecisionRecordFileProblem this[int index] => _problems[index];

    /// <inheritdoc />
    public IEnumerator<DecisionRecordFileProblem> GetEnumerator() => ((IEnumerable<DecisionRecordFileProblem>)_problems).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
