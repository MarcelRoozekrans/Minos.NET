using System.Runtime.InteropServices;

namespace Minos;

/// <summary>One parsed answer, as plain numbers: <see cref="Answers"/> rebuilds the typed value from it on demand.</summary>
/// <param name="ValueIndex">The chosen option's or most probable level's index; 0 for a Noul.</param>
/// <param name="Value">A Noul's probability, or a Score's expected level; 0 for a Choice.</param>
/// <param name="Confidence">A Choice's or Score's confidence; 0 for a Noul.</param>
/// <param name="Offset">The start of the answer's slice of the shared probability buffer.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct AnswerSlot(int ValueIndex, double Value, double Confidence, int Offset);
