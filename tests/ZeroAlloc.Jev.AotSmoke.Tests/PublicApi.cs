namespace ZeroAlloc.Jev.AotSmoke.Tests;

/// <summary>
/// The public entry points both packages declare in their PublicAPI files: every public constructor and method, with
/// its signature exactly as RS0016 prints it, after applying the <c>*REMOVED*</c> lines.
/// </summary>
/// <remarks>
/// <para>A PublicAPI line that is not an entry point is excluded with the reason <see cref="ExclusionReason"/> gives:</para>
/// <list type="bullet">
/// <item>Type declarations: a type is not called; its constructors and methods are its entry points.</item>
/// <item>Property and indexer accessors, fields, constants and enum members: they are data, read or written through
/// the entry points that take or return them, and are covered through their types.</item>
/// <item>Operators, user-defined conversions included: they are syntax over an entry point, such as <c>==</c> over a
/// declared <c>Equals(T)</c>, or over a factory such as <c>JevContent.FromString</c>.</item>
/// <item><c>override</c>s of <see cref="object"/>'s <c>Equals(object)</c>, <c>GetHashCode()</c> and
/// <c>ToString()</c>: they are the runtime's contract, not this library's API.</item>
/// <item>The members the compiler generates for a record class: <c>&lt;Clone&gt;$</c>, <c>Deconstruct</c>,
/// <c>PrintMembers</c> and <c>Equals(T? other)</c>. A record class is recognised by its <c>&lt;Clone&gt;$</c> line,
/// which only the compiler can declare. A struct has no such line, so its <c>Equals(T other)</c> is the one its
/// author wrote for <see cref="IEquatable{T}"/>, and it counts.</item>
/// </list>
/// <para>Abstract members and the default interface methods on <c>IJevClient</c> are entry points like any other.</para>
/// </remarks>
internal static class PublicApi
{
    private const string Arrow = " -> ";
    private const string RemovedPrefix = "*REMOVED*";

    private static readonly string[][] Packages =
    [
        ["src/ZeroAlloc.Jev/PublicAPI.Shipped.txt", "src/ZeroAlloc.Jev/PublicAPI.Unshipped.txt"],
        [
            "src/ZeroAlloc.Jev.DependencyInjection/PublicAPI.Shipped.txt",
            "src/ZeroAlloc.Jev.DependencyInjection/PublicAPI.Unshipped.txt",
        ],
    ];

    private static readonly string[] ObjectMembers = ["Equals(object? obj)", "GetHashCode()", "ToString()"];

    /// <summary>Every line both packages declare, after applying the <c>*REMOVED*</c> lines.</summary>
    public static List<string> Lines { get; } = ReadLines();

    /// <summary>The record classes: the types with a <c>&lt;Clone&gt;$</c> line, which only the compiler declares.</summary>
    // Initialised after Lines and before EntryPoints, which reads it.
    private static HashSet<string> RecordClasses { get; } = FindRecordClasses();

    /// <summary>The entry points: every line <see cref="ExclusionReason"/> keeps.</summary>
    public static List<string> EntryPoints { get; } = [.. Lines.Where(line => ExclusionReason(line) is null)];

    /// <summary>Why <paramref name="line"/> is not an entry point, or <see langword="null"/> when it is one.</summary>
    public static string? ExclusionReason(string line)
    {
        var arrow = line.IndexOf(Arrow, StringComparison.Ordinal);
        if (arrow < 0)
        {
            return "a type declaration; its constructors and methods are its entry points";
        }

        var declaration = StripModifiers(line[..arrow], out var modifiers);
        if (modifiers.Contains("const", StringComparer.Ordinal))
        {
            return "a constant, covered through its type";
        }

        if (declaration.EndsWith(".get", StringComparison.Ordinal)
            || declaration.EndsWith(".set", StringComparison.Ordinal)
            || declaration.EndsWith(".init", StringComparison.Ordinal))
        {
            return "a property or indexer accessor, covered through its type";
        }

        var open = declaration.IndexOf('(', StringComparison.Ordinal);
        if (open < 0)
        {
            return "a field or enum member, covered through its type";
        }

        if (declaration[..open].Contains("operator ", StringComparison.Ordinal))
        {
            return "an operator or a user-defined conversion, syntax over the entry points it calls";
        }

        var (containingType, member) = Split(declaration[..open]);

        var signature = member + declaration[open..];
        if (modifiers.Contains("override", StringComparer.Ordinal) && ObjectMembers.Contains(signature, StringComparer.Ordinal))
        {
            return "an override of an object member";
        }

        if (RecordClasses.Contains(containingType))
        {
            if (string.Equals(member, "<Clone>$", StringComparison.Ordinal)
                || string.Equals(member, "Deconstruct", StringComparison.Ordinal)
                || string.Equals(member, "PrintMembers", StringComparison.Ordinal)
                || string.Equals(signature, "Equals(" + containingType + "? other)", StringComparison.Ordinal))
            {
                return "a member the compiler generates for a record class";
            }
        }

        return null;
    }

    private static HashSet<string> FindRecordClasses()
        => Lines
            .Select(line => (Line: line, Clone: line.IndexOf(".<Clone>$()", StringComparison.Ordinal)))
            .Where(found => found.Clone > 0)
            .Select(found => StripModifiers(found.Line[..found.Clone], out _))
            .ToHashSet(StringComparer.Ordinal);

    private static List<string> ReadLines()
    {
        var lines = new List<string>();
        foreach (var files in Packages)
        {
            lines.AddRange(ReadPackage(files));
        }

        return lines;
    }

    // One package's lines, Shipped and Unshipped together, so an Unshipped *REMOVED* line removes its Shipped line.
    private static List<string> ReadPackage(string[] files)
    {
        var package = files
            .SelectMany(file => File.ReadAllLines(Path.Combine(Repository.Root, file)))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#nullable", StringComparison.Ordinal))
            .ToList();
        var removed = package
            .Where(line => line.StartsWith(RemovedPrefix, StringComparison.Ordinal))
            .Select(line => line[RemovedPrefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
        return [.. package.Where(line => !line.StartsWith(RemovedPrefix, StringComparison.Ordinal) && !removed.Contains(line))];
    }

    // Leading modifiers, such as "static", "abstract" or "override", come before the qualified name and are lower case.
    private static string StripModifiers(string text, out List<string> modifiers)
    {
        modifiers = [];
        while (true)
        {
            var space = text.IndexOf(' ', StringComparison.Ordinal);
            var dot = text.IndexOf('.', StringComparison.Ordinal);
            var paren = text.IndexOf('(', StringComparison.Ordinal);
            if (space <= 0 || (dot >= 0 && dot < space) || (paren >= 0 && paren < space) || !IsLowerCaseWord(text[..space]))
            {
                return text;
            }

            modifiers.Add(text[..space]);
            text = text[(space + 1)..];
        }
    }

    private static bool IsLowerCaseWord(string word)
    {
        foreach (var c in word)
        {
            if (!char.IsAsciiLetterLower(c))
            {
                return false;
            }
        }

        return true;
    }

    // Splits "Namespace.Type<T>.Member" at the last '.' outside angle brackets, so "<Clone>$" and generic
    // containing types such as "Choice<T>" stay whole.
    private static (string ContainingType, string Member) Split(string qualified)
    {
        var depth = 0;
        var split = -1;
        for (var i = 0; i < qualified.Length; i++)
        {
            switch (qualified[i])
            {
                case '<':
                    depth++;
                    break;
                case '>':
                    depth--;
                    break;
                case '.' when depth == 0:
                    split = i;
                    break;
            }
        }

        return (qualified[..split], qualified[(split + 1)..]);
    }
}
