using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// A default interface method counts as called only when its own body runs. These tests run the coverage resolver
/// over a small compilation, so they pin the rule independently of what the smoke app happens to call.
/// </summary>
public sealed class DefaultInterfaceMethodTests
{
    private const string Source = """
        public interface IGreeter
        {
            string Name();

            string Greet() => "Hello, " + Name();
        }

        public sealed class Plain : IGreeter
        {
            public string Name() => "plain";
        }

        public sealed class Overriding : IGreeter
        {
            public string Name() => "overriding";

            public string Greet() => "Hi";
        }

        public static class Checks
        {
            public static string OnAPlainLocal()
            {
                IGreeter greeter = new Plain();
                return greeter.Greet();
            }

            public static string OnAPlainCreation() => ((IGreeter)new Plain()).Greet();

            public static string OnAnOverridingLocal()
            {
                IGreeter greeter = new Overriding();
                return greeter.Greet();
            }

            public static string OnAnUnknownReceiver(IGreeter greeter) => greeter.Greet();

            public static System.Func<string> AsAMethodGroup()
            {
                IGreeter greeter = new Plain();
                return greeter.Greet;
            }

            public static string AbstractMemberOnAnUnknownReceiver(IGreeter greeter) => greeter.Name();
        }
        """;

    private static readonly Compilation Compilation = CSharpCompilation.Create(
        "DefaultInterfaceMethods",
        [CSharpSyntaxTree.ParseText(Source)],
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void The_sample_compiles() => Assert.Empty(Compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

    [Theory]
    [InlineData("OnAPlainLocal")]
    [InlineData("OnAPlainCreation")]
    public void A_default_called_on_a_type_that_does_not_override_it_counts(string check)
        => Assert.Contains(Greet, Calls(check), SymbolEqualityComparer.Default);

    [Theory]
    [InlineData("OnAnOverridingLocal")]
    [InlineData("OnAnUnknownReceiver")]
    [InlineData("AsAMethodGroup")]
    public void A_default_whose_body_may_not_run_does_not_count(string check)
        => Assert.DoesNotContain(Greet, Calls(check), SymbolEqualityComparer.Default);

    [Fact]
    public void An_abstract_interface_member_counts_on_any_receiver()
        => Assert.Contains(Member("IGreeter", "Name"), Calls("AbstractMemberOnAnUnknownReceiver"), SymbolEqualityComparer.Default);

    private static IMethodSymbol Greet => Member("IGreeter", "Greet");

    private static HashSet<IMethodSymbol> Calls(string check)
        => SmokeChecks.Reach(Compilation, Member("Checks", check), transitive: true);

    private static IMethodSymbol Member(string type, string name)
        => Compilation.GetTypeByMetadataName(type)!.GetMembers(name).OfType<IMethodSymbol>().First();
}
