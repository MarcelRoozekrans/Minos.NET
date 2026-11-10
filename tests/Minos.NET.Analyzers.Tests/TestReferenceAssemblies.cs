using Microsoft.CodeAnalysis.Testing;

namespace Minos.Analyzers.Tests;

/// <summary>The reference assemblies the test sources compile against: those of the runtime the tests run on.</summary>
internal static class TestReferenceAssemblies
{
    // Minos.NET is built for the same runtime as the test project, and a compilation cannot reference an assembly that was
    // built against a newer System.Runtime than its reference assemblies. The testing package has no entry for net11.0 yet,
    // so it is declared the way the package declares ReferenceAssemblies.Net.Net100.
#if NET11_0_OR_GREATER
    internal static ReferenceAssemblies Current { get; } = new("net11.0", new PackageIdentity("Microsoft.NETCore.App.Ref", "11.0.0"), Path.Combine("ref", "net11.0"));
#else
    internal static ReferenceAssemblies Current => ReferenceAssemblies.Net.Net100;
#endif
}
