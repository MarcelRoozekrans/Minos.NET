namespace Minos;

/// <summary>Settings for the cache stage that <see cref="CachingDecisionClientBuilderExtensions.UseCaching"/> adds.</summary>
public sealed class DecisionCacheOptions
{
    // An expiry is added to the current time when an entry is stored, so an unbounded one overflows there, after the provider was paid.
    private static readonly TimeSpan MaxExpiration = TimeSpan.FromDays(365);

    /// <summary>Gets or sets how long an answer is kept, in the distributed cache and, unless <see cref="LocalCacheExpiration"/> is set, in process.</summary>
    /// <value>Defaults to 5 minutes. At most 365 days.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative or more than 365 days.</exception>
    public TimeSpan Expiration
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxExpiration);
            field = value;
        }
    } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets how long an answer is kept in the in-process cache, or <see langword="null"/> for <see cref="Expiration"/>.</summary>
    /// <value>Defaults to <see langword="null"/>. At most 365 days.</value>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero, negative or more than 365 days.</exception>
    public TimeSpan? LocalCacheExpiration
    {
        get;
        set
        {
            if (value is { } local)
            {
                ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(local, TimeSpan.Zero, nameof(value));
                ArgumentOutOfRangeException.ThrowIfGreaterThan(local, MaxExpiration, nameof(value));
            }

            field = value;
        }
    }

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

    /// <summary>
    /// Gets or sets whether the stage uses a distributed cache. Set it to <see langword="false"/> when the app's
    /// <see cref="Microsoft.Extensions.Caching.Distributed.IDistributedCache"/> serves something else, such as session
    /// state: the stage then caches in process only, and <see cref="CachingDecisionClientBuilderExtensions.UseCaching"/>
    /// takes no distributed cache from the services.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool UseDistributedCache { get; set; } = true;
}
