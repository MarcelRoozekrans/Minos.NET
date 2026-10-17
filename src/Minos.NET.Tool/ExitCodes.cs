namespace Minos.Tool;

/// <summary>The process exit codes of the minos tool.</summary>
internal static class ExitCodes
{
    /// <summary>The command did what it was asked.</summary>
    public const int Success = 0;

    /// <summary>A run-time failure: a stopped run, a failed check, a network fault or an output directory that cannot be written.</summary>
    public const int Failure = 1;

    /// <summary>The input, the usage or the configuration is wrong.</summary>
    public const int Usage = 2;

    /// <summary>The user cancelled the command.</summary>
    public const int Cancelled = 130;
}
