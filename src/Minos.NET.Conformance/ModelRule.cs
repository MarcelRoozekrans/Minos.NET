namespace Minos.Conformance;

/// <summary>How a target's reported model must relate to the requested one, for check R2. The report states <see cref="Description"/>.</summary>
public sealed class ModelRule
{
    private const string OpenRouterPrefix = "typesafe/";
    private const string OpenRouterVariantPrefix = "~typesafe/";

    private readonly Func<string, string, bool> _matches;

    /// <summary>Initializes a new instance of the <see cref="ModelRule"/> class.</summary>
    /// <param name="description">The rule in one sentence, for the report.</param>
    /// <param name="matches">Whether a reported model, the second argument, fits the requested one, the first.</param>
    public ModelRule(string description, Func<string, string, bool> matches)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(matches);

        Description = description;
        _matches = matches;
    }

    /// <summary>
    /// Gets TypeSafe's rule: the reported model is the requested one or, for an alias such as <c>jev-latest</c>, a
    /// versioned id of the same family, such as <c>jev-1.13.0</c>.
    /// </summary>
    public static ModelRule TypeSafe { get; } = new(
        "The reported model is the requested one or, for an alias such as jev-latest, a versioned id of the same family, such as jev-1.13.0.",
        SameOrVersioned);

    /// <summary>
    /// Gets OpenRouter's rule: TypeSafe's, with the reported model prefixed with <c>typesafe/</c> or <c>~typesafe/</c>,
    /// such as <c>~typesafe/jev-latest</c> or <c>typesafe/jev-1.13-20260917</c>.
    /// </summary>
    public static ModelRule OpenRouter { get; } = new(
        "The reported model is the requested one or a versioned id of its family, prefixed with typesafe/ or ~typesafe/, such as ~typesafe/jev-latest or typesafe/jev-1.13-20260917.",
        static (requested, reported) => WithoutPrefix(reported) is { } bare && SameOrVersioned(WithoutPrefix(requested) ?? requested, bare));

    /// <summary>Gets the rule in one sentence.</summary>
    public string Description { get; }

    /// <summary>Returns whether <paramref name="reported"/> fits <paramref name="requested"/> by this rule.</summary>
    /// <param name="requested">The model the probe asked for.</param>
    /// <param name="reported">The model the response named.</param>
    /// <returns><see langword="true"/> when it fits.</returns>
    public bool Matches(string requested, string reported)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(reported);
        return _matches(requested, reported);
    }

    /// <summary>
    /// Whether <paramref name="reported"/> is <paramref name="requested"/>, or <paramref name="requested"/> is an alias, whose
    /// last dash-separated part is letters only, and <paramref name="reported"/> continues its family with a digit.
    /// </summary>
    internal static bool SameOrVersioned(string requested, string reported)
    {
        if (string.Equals(requested, reported, StringComparison.Ordinal))
        {
            return true;
        }

        var dash = requested.LastIndexOf('-');
        if (dash <= 0 || dash == requested.Length - 1 || !IsLetters(requested.AsSpan(dash + 1)))
        {
            return false;
        }

        var family = requested.AsSpan(0, dash + 1);
        return reported.Length > family.Length
            && reported.AsSpan().StartsWith(family, StringComparison.Ordinal)
            && char.IsAsciiDigit(reported[family.Length]);
    }

    private static string? WithoutPrefix(string model)
        => model.StartsWith(OpenRouterVariantPrefix, StringComparison.Ordinal) ? model[OpenRouterVariantPrefix.Length..]
            : model.StartsWith(OpenRouterPrefix, StringComparison.Ordinal) ? model[OpenRouterPrefix.Length..]
            : null;

    private static bool IsLetters(ReadOnlySpan<char> text)
    {
        foreach (var c in text)
        {
            if (!char.IsAsciiLetter(c))
            {
                return false;
            }
        }

        return true;
    }
}
