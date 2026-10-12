namespace Minos.Samples;

/// <summary>The command line of a sample that runs against several providers: its mode, and the provider to use.</summary>
public sealed record SampleArguments(SampleMode Mode, string? Provider)
{
    /// <summary>
    /// Parses at most one of <c>--replay</c>, <c>--live</c> or <c>--record</c>, and at most one <c>--provider &lt;name&gt;</c>,
    /// in any order. No mode means <see cref="SampleMode.Replay"/>; no provider means the one configuration names.
    /// </summary>
    /// <exception cref="ArgumentException">An argument is unknown, repeated, or <c>--provider</c> has no name after it.</exception>
    public static SampleArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        SampleMode? mode = null;
        string? provider = null;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--replay" or "--live" or "--record":
                    if (mode is not null)
                    {
                        throw new ArgumentException("Pass at most one of --replay, --live or --record.", nameof(args));
                    }

                    mode = SampleModes.Parse([args[i]]);
                    break;
                case "--provider":
                    if (provider is not null)
                    {
                        throw new ArgumentException("Pass --provider at most once.", nameof(args));
                    }

                    if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("--provider needs the name of a provider, such as --provider local.", nameof(args));
                    }

                    i++;
                    provider = args[i];
                    break;
                default:
                    throw new ArgumentException(
                        "Unknown argument " + args[i] + ". Use --replay, --live, --record or --provider <name>.", nameof(args));
            }
        }

        return new SampleArguments(mode ?? SampleMode.Replay, provider);
    }
}
