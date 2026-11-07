#:package System.Reflection.MetadataLoadContext
#:property PublishAot=false
// Usage: dotnet run tools/upstream/HasType.cs -- <assembly.dll> <full.type.Name>
// Exit 0: the assembly exports a PUBLIC type with that full name. Exit 1: it does not.
// Exit 2: bad arguments or the assembly cannot be read.
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
    return type is { IsPublic: true } ? 0 : 1;
}
catch (Exception e) when (e is IOException or BadImageFormatException or FileLoadException or ArgumentException)
{
    Console.Error.WriteLine($"HasType: {e.Message}");
    return 2;
}
