namespace Minos;

/// <summary>How a built set reads one question's answer.</summary>
/// <param name="Kind">The question's kind.</param>
/// <param name="Key">The wire key, for the missing-answer message.</param>
/// <param name="Options">
/// The options or levels, an <see cref="EnumOptionSet{T}"/> or a <see cref="KeyedOptionSet"/>; <see langword="null"/> for a
/// Noul.
/// </param>
/// <param name="Offset">The start of this question's slice of the shared probability buffer.</param>
internal readonly record struct QuestionPlan(QuestionKind Kind, string Key, object? Options, int Offset);
