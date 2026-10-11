using System.Text.Json;

namespace Minos.Transport;

/// <summary>Writes the state value at the writer's current position; the request writer owns everything around it.</summary>
/// <remarks>
/// With <c>asText</c> the state is written as one JSON string: the text itself, or the JSON state's raw text. A protocol
/// that needs text, such as a content part, asks for it. Otherwise the state is written as the JSON value it is.
/// </remarks>
internal delegate void StateWriter<TArg>(Utf8JsonWriter writer, RawJson body, TArg arg, bool asText)
    where TArg : allows ref struct;
