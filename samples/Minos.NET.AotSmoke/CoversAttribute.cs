namespace Minos.AotSmoke;

/// <summary>
/// Declares one public entry point a smoke check calls, as its line in a PublicAPI file prints it. The coverage test in
/// tests/Minos.NET.AotSmoke.Tests fails unless every public entry point is declared by a check that
/// <see cref="Program"/>'s <c>Main</c> runs, and unless each check really calls what it declares.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
internal sealed class CoversAttribute(string entryPoint) : Attribute
{
    /// <summary>The entry point's signature, exactly as its PublicAPI line prints it.</summary>
    public string EntryPoint { get; } = entryPoint;
}
