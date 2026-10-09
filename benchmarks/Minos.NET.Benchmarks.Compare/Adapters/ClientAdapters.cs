namespace Minos.Benchmarks.Compare.Adapters;

/// <summary>Every benchmark client, by name, in the order the harness runs them.</summary>
public static class ClientAdapters
{
    /// <summary>
    /// The Minos.NET client. Its id stays <c>zeroalloc-jev</c>, the key the published results in
    /// <c>benchmarks/compare/results</c> and the docs' tables were measured under, so new runs merge with them.
    /// </summary>
    public const string Minos = "zeroalloc-jev";

    /// <summary>The hand-written <see cref="HttpClient"/> and System.Text.Json client.</summary>
    public const string Raw = "raw-httpclient";

    /// <summary>The JevSharp client.</summary>
    public const string JevSharp = "jevsharp";

    /// <summary>The TypeSafe.AI.Sdk client.</summary>
    public const string TypeSafeSdk = "typesafe-ai-sdk";

    /// <summary>The Jev.Net client.</summary>
    public const string JevNet = "jev-net";

    /// <summary>Gets every client name, in run order.</summary>
    public static IReadOnlyList<string> All { get; } = [Minos, Raw, JevSharp, TypeSafeSdk, JevNet];

    /// <summary>Creates the named client, pointed at <paramref name="baseAddress"/>.</summary>
    /// <param name="client">One of <see cref="All"/>.</param>
    /// <param name="baseAddress">The mock's root address.</param>
    /// <returns>A new client; the caller disposes it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="client"/> is not a known client.</exception>
    public static IClientAdapter Create(string client, Uri baseAddress) => client switch
    {
        Minos => new MinosAdapter(baseAddress),
        Raw => new RawHttpAdapter(baseAddress),
        JevSharp => new JevSharpAdapter(baseAddress),
        TypeSafeSdk => new TypeSafeSdkAdapter(baseAddress),
        JevNet => new JevNetAdapter(baseAddress),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "Unknown benchmark client."),
    };

    /// <summary>Returns <paramref name="baseAddress"/> with a trailing slash, so relative paths resolve under it.</summary>
    /// <param name="baseAddress">An absolute root address.</param>
    /// <returns>The same address, ending in <c>/</c>.</returns>
    public static Uri WithTrailingSlash(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        var text = baseAddress.AbsoluteUri;
        return text.EndsWith('/') ? baseAddress : new Uri(text + "/");
    }

    /// <summary>Returns <paramref name="baseAddress"/> without a trailing slash, for clients that append <c>/v1/systemone</c>.</summary>
    /// <param name="baseAddress">An absolute root address.</param>
    /// <returns>The address as text, not ending in <c>/</c>.</returns>
    public static string WithoutTrailingSlash(Uri baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        return baseAddress.AbsoluteUri.TrimEnd('/');
    }

    /// <summary>Returns the <c>v1/systemone</c> endpoint under <paramref name="baseAddress"/>.</summary>
    /// <param name="baseAddress">An absolute root address.</param>
    /// <returns>The full endpoint.</returns>
    public static Uri SystemOneEndpoint(Uri baseAddress) => new(WithTrailingSlash(baseAddress), "v1/systemone");
}
