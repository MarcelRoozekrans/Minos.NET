namespace Minos.Caching;

/// <summary>Settings for the cache stage that <see cref="CachingDecisionClientBuilderExtensions.UseHybridCache"/> adds.</summary>
public sealed class DecisionCacheOptions
{
    /// <summary>Gets or sets how long an answer stays in the distributed cache, or <see langword="null"/> for the cache's default.</summary>
    public TimeSpan? Expiration { get; set; }

    /// <summary>Gets or sets how long an answer stays in the in-process cache, or <see langword="null"/> for the cache's default.</summary>
    public TimeSpan? LocalCacheExpiration { get; set; }

    /// <summary>Gets or sets the text every key starts with, so apps sharing one distributed cache can keep their entries apart.</summary>
    /// <value>Defaults to <c>minos:</c>.</value>
    /// <exception cref="ArgumentNullException">The value is <see langword="null"/>.</exception>
    public string KeyPrefix
    {
        get;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            field = value;
        }
    } = "minos:";
}
