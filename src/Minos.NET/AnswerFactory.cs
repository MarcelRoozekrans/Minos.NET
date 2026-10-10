namespace Minos;

/// <summary>Builds a result from the answers a protocol read.</summary>
internal delegate TResult AnswerFactory<TResult>(AnswerSlots answers);
