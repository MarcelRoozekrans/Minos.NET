namespace ZeroAlloc.Jev.Benchmarks.Compare;

/// <summary>The harness's command line: <c>--base-url &lt;url&gt; --out &lt;dir&gt; [--smoke] [--machine &lt;name&gt;]</c>.</summary>
/// <param name="BaseUrl">The mock's root address.</param>
/// <param name="OutDirectory">Where the result file and BenchmarkDotNet's artifacts go.</param>
/// <param name="Smoke">Whether this is a smoke run: BenchmarkDotNet's dry job and a short throughput run.</param>
/// <param name="Machine">The machine name the result file reports and is named after.</param>
public sealed record CompareOptions(Uri BaseUrl, string OutDirectory, bool Smoke, string Machine)
{
    /// <summary>The usage line printed when the arguments are wrong.</summary>
    public const string Usage =
        "Usage: dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks.Compare -- --base-url <url> --out <dir> [--smoke] [--machine <name>]";

    /// <summary>Parses the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="defaultMachine">The machine name to use when <c>--machine</c> is absent.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An argument is missing, unknown or invalid.</exception>
    public static CompareOptions Parse(IReadOnlyList<string> args, string defaultMachine)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? baseUrl = null;
        string? outDirectory = null;
        string? machine = null;
        var smoke = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--base-url":
                    baseUrl = Value(args, ref i);
                    break;
                case "--out":
                    outDirectory = Value(args, ref i);
                    break;
                case "--machine":
                    machine = Value(args, ref i);
                    break;
                case "--smoke":
                    smoke = true;
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + args[i], nameof(args));
            }
        }

        if (baseUrl is null || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)))
        {
            throw new ArgumentException("--base-url must be an absolute http or https address.", nameof(args));
        }

        if (string.IsNullOrWhiteSpace(outDirectory))
        {
            throw new ArgumentException("--out is required.", nameof(args));
        }

        machine ??= defaultMachine;
        if (string.IsNullOrWhiteSpace(machine))
        {
            throw new ArgumentException("--machine must not be blank.", nameof(args));
        }

        return new CompareOptions(uri, outDirectory, smoke, machine.Trim());
    }

    /// <summary>Gets the result file's name, <c>dotnet-&lt;machine&gt;.json</c>, with characters unsafe in a file name replaced.</summary>
    public string ResultFileName
    {
        get
        {
            var safe = string.Create(Machine.Length, Machine, static (span, machine) =>
            {
                for (var i = 0; i < machine.Length; i++)
                {
                    var c = machine[i];
                    span[i] = char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-';
                }
            });
            return "dotnet-" + safe + ".json";
        }
    }

    private static string Value(IReadOnlyList<string> args, ref int i)
    {
        if (i + 1 >= args.Count)
        {
            throw new ArgumentException(args[i] + " needs a value.", nameof(args));
        }

        i++;
        return args[i];
    }
}
