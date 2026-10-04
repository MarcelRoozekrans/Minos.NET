using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace ZeroAlloc.Jev.Benchmarks.Shared;

/// <summary>
/// The <c>--cores</c> option the mock and the harness share, so a runner can give each its own half of the CPU cores.
/// It takes a hexadecimal mask such as <c>0xF0</c>, or a list of core indexes and ranges such as <c>4-7</c> or
/// <c>0,2,4-6</c>, and pins the current process to those cores on Windows and Linux.
/// </summary>
/// <remarks>Compiled into both projects from one file, so the two parse the option the same way.</remarks>
internal static class CoreAffinity
{
    /// <summary>The highest core index a mask can hold.</summary>
    public const int MaxCoreIndex = 63;

    /// <summary>Parses a mask or a core list into a bit mask, bit <c>n</c> for core <c>n</c>.</summary>
    /// <param name="text">A mask such as <c>0xF0</c>, or a list such as <c>0,2,4-6</c>.</param>
    /// <returns>The mask; never zero.</returns>
    /// <exception cref="ArgumentException">The text is empty, malformed, names no core or a core above 63.</exception>
    public static ulong Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var trimmed = text.Trim();
        ulong mask;
        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (!ulong.TryParse(trimmed.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out mask))
            {
                throw Invalid(text);
            }
        }
        else
        {
            mask = ParseList(text, trimmed);
        }

        return mask == 0 ? throw new ArgumentException("--cores names no core.", nameof(text)) : mask;
    }

    /// <summary>Describes a mask as a canonical core list, such as <c>0-3,8</c>.</summary>
    /// <param name="mask">A mask with at least one bit set.</param>
    /// <returns>The list.</returns>
    public static string Describe(ulong mask)
    {
        var text = new StringBuilder();
        var core = 0;
        while (core <= MaxCoreIndex)
        {
            if ((mask & (1UL << core)) == 0)
            {
                core++;
                continue;
            }

            var end = core;
            while (end < MaxCoreIndex && (mask & (1UL << (end + 1))) != 0)
            {
                end++;
            }

            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(core.ToString(CultureInfo.InvariantCulture));
            if (end > core)
            {
                text.Append('-').Append(end.ToString(CultureInfo.InvariantCulture));
            }

            core = end + 1;
        }

        return text.ToString();
    }

    /// <summary>Pins the current process, and the processes it starts, to the cores in <paramref name="mask"/>.</summary>
    /// <param name="mask">The cores, from <see cref="Parse"/>.</param>
    /// <exception cref="ArgumentException">The mask names a core this machine does not have.</exception>
    /// <exception cref="PlatformNotSupportedException">The operating system is neither Windows nor Linux.</exception>
    public static void Apply(ulong mask)
    {
        var highest = 63 - BitOperations.LeadingZeroCount(mask);
        if (highest >= Environment.ProcessorCount)
        {
            throw new ArgumentException(
                string.Create(CultureInfo.InvariantCulture, $"--cores names core {highest}, but this machine has cores 0 to {Environment.ProcessorCount - 1}."),
                nameof(mask));
        }

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            using var process = Process.GetCurrentProcess();
            process.ProcessorAffinity = unchecked((nint)mask);
            return;
        }

        throw new PlatformNotSupportedException("--cores is supported on Windows and Linux only.");
    }

    private static ulong ParseList(string text, string trimmed)
    {
        ulong mask = 0;
        foreach (var part in trimmed.Split(',', StringSplitOptions.TrimEntries))
        {
            var dash = part.IndexOf('-', StringComparison.Ordinal);
            var first = Core(text, dash < 0 ? part : part[..dash]);
            var last = dash < 0 ? first : Core(text, part[(dash + 1)..]);
            if (last < first)
            {
                throw Invalid(text);
            }

            for (var core = first; core <= last; core++)
            {
                mask |= 1UL << core;
            }
        }

        return mask;
    }

    private static int Core(string text, string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var core) && core <= MaxCoreIndex
            ? core
            : throw Invalid(text);

    private static ArgumentException Invalid(string text)
        => new("--cores must be a hexadecimal mask such as 0xF0 or a core list such as 0-3,8, not '" + text + "'.", nameof(text));
}
