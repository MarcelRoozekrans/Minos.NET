namespace Minos.Samples;

/// <summary>How a sample talks to Jev.</summary>
public enum SampleMode
{
    /// <summary>Answer from the sample's checked-in recordings; no network and no key. The default.</summary>
    Replay,

    /// <summary>Call the API; needs a key.</summary>
    Live,

    /// <summary>Call the API and save every successful response body to the sample's recordings.</summary>
    Record,
}

/// <summary>Reads a sample's mode from its command line.</summary>
public static class SampleModes
{
    /// <summary>Parses <c>--replay</c>, <c>--live</c> or <c>--record</c>; no argument means <see cref="SampleMode.Replay"/>.</summary>
    /// <exception cref="ArgumentException">An argument is not one of the three, or more than one is given.</exception>
    public static SampleMode Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Count == 0)
        {
            return SampleMode.Replay;
        }

        if (args.Count > 1)
        {
            throw new ArgumentException("Pass at most one of --replay, --live or --record.", nameof(args));
        }

        return args[0] switch
        {
            "--replay" => SampleMode.Replay,
            "--live" => SampleMode.Live,
            "--record" => SampleMode.Record,
            _ => throw new ArgumentException("Unknown argument " + args[0] + ". Use --replay, --live or --record.", nameof(args)),
        };
    }
}
