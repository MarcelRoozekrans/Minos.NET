using System.Text.Json;

namespace Minos.Transport;

/// <summary>Writes the state value at the writer's current position; the request writer owns everything around it.</summary>
internal delegate void StateWriter<TArg>(Utf8JsonWriter writer, RawJson body, TArg arg)
    where TArg : allows ref struct;
