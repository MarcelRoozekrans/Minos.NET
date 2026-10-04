using ZeroAlloc.Jev.Benchmarks.Shared;

namespace ZeroAlloc.Jev.Benchmarks.Mock;

/// <summary>The mock's command line: <c>[--port &lt;port&gt;] [--cores &lt;mask or list&gt;]</c>.</summary>
/// <param name="Port">The port to listen on; 5005 unless <c>--port</c> says otherwise.</param>
/// <param name="Cores">The cores to pin the process to, as a bit mask, or <see langword="null"/> for every core.</param>
public sealed record MockOptions(int Port, ulong? Cores)
{
    /// <summary>The port the mock listens on by default.</summary>
    public const int DefaultPort = 5005;

    /// <summary>The usage line printed when the arguments are wrong.</summary>
    public const string Usage = "Usage: dotnet run -c Release --project benchmarks/ZeroAlloc.Jev.Benchmarks.Mock -- [--port <port>] [--cores <mask or list>]";

    /// <summary>Parses the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The options.</returns>
    /// <exception cref="ArgumentException">An option is unknown, has no value, or has a bad value.</exception>
    public static MockOptions Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var port = DefaultPort;
        ulong? cores = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--port":
                    port = ArgumentReader.Port("--port", ArgumentReader.Value(args, ref i));
                    break;
                case "--cores":
                    cores = CoreAffinity.Parse(ArgumentReader.Value(args, ref i));
                    break;
                default:
                    throw new ArgumentException("Unknown argument: " + args[i], nameof(args));
            }
        }

        return new MockOptions(port, cores);
    }
}
