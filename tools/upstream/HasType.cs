#:package System.Reflection.MetadataLoadContext
#:property PublishAot=false
// Usage: dotnet run tools/upstream/HasType.cs -- <assembly.dll> <full.type.Name>
// A definite answer is a line FOUND (a PUBLIC type with that full name) or ABSENT on stdout with exit 0.
// Anything else, including any exception, is an error: exit 2 and no answer line.
using System.Reflection;

if (args.Length != 2) { Console.Error.WriteLine("usage: HasType <assembly.dll> <full.type.Name>"); return 2; }
try
{
    var path = Path.GetFullPath(args[0]);
    var coreDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
    var resolver = new PathAssemblyResolver(Directory.GetFiles(coreDir, "*.dll").Append(path));
    using var mlc = new MetadataLoadContext(resolver);
    var asm = mlc.LoadFromAssemblyPath(path);
    var type = asm.GetType(args[1], throwOnError: false);
    Console.WriteLine(type is { IsPublic: true } ? "FOUND" : "ABSENT");
    return 0;
}
catch (Exception e)
{
    Console.Error.WriteLine($"HasType: {e.Message}");
    return 2;
}
