namespace Minos.PackTests;

/// <summary>
/// The collection every pack test class joins, so their fixtures run <c>dotnet pack</c> one at a time: a pack that
/// finds no build output builds the project, and two such builds of <c>Minos.NET</c> would race.
/// </summary>
[CollectionDefinition(Name)]
public sealed class Packing
{
    /// <summary>The collection's name, for <see cref="CollectionAttribute"/>.</summary>
    public const string Name = "Packing";
}
