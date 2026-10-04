namespace ZeroAlloc.Jev.Benchmarks.Shared;

/// <summary>
/// The argument reading the mock and the harness share, so both treat a missing or bad value the same way: an
/// <see cref="ArgumentException"/> whose message the process prints before it exits with code 2.
/// </summary>
/// <remarks>Compiled into both projects from one file.</remarks>
internal static class ArgumentReader
{
    /// <summary>Returns the value after the option at <paramref name="index"/>, and moves <paramref name="index"/> onto it.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="index">The option's index; on return, the value's index.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">The option is the last argument, so it has no value.</exception>
    public static string Value(IReadOnlyList<string> args, ref int index)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (index + 1 >= args.Count)
        {
            throw new ArgumentException(args[index] + " needs a value.", nameof(args));
        }

        index++;
        return args[index];
    }

    /// <summary>Parses a TCP port, 0 to 65535, where 0 picks a free port.</summary>
    /// <param name="option">The option's name, for the message.</param>
    /// <param name="value">The text to parse.</param>
    /// <returns>The port.</returns>
    /// <exception cref="ArgumentException">The text is not a port number.</exception>
    public static int Port(string option, string value)
        => int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port) && port <= 65535
            ? port
            : throw new ArgumentException(option + " must be a port number from 0 to 65535, not '" + value + "'.", nameof(value));
}
