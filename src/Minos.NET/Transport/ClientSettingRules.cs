using System.Globalization;

namespace Minos.Transport;

/// <summary>
/// The option rules every protocol package's client settings share, with the same messages: the per-attempt time-out,
/// the base address from an option or an environment variable, the API key's characters, and the capability overrides.
/// </summary>
internal static class ClientSettingRules
{
    /// <summary>Checks the per-attempt time-out: positive or infinite, and at most <see cref="int.MaxValue"/> milliseconds.</summary>
    /// <param name="timeout">The time-out.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <exception cref="ArgumentException">The time-out is zero or negative, and not infinite.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The time-out exceeds <see cref="int.MaxValue"/> milliseconds.</exception>
    public static void CheckTimeout(TimeSpan timeout, string paramName)
    {
        if (timeout <= TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentException("The time-out must be positive.", paramName);
        }

        if (timeout > RetrySettings.MaxMilliseconds)
        {
            throw new ArgumentOutOfRangeException(paramName, timeout, "The time-out must not exceed " + RetrySettings.MaxMilliseconds.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + " milliseconds.");
        }
    }

    /// <summary>Checks a base address set as an option: absolute, without a query or fragment, http or https.</summary>
    /// <param name="address">The option's value.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <returns><paramref name="address"/>.</returns>
    /// <exception cref="ArgumentException">The address breaks a rule.</exception>
    public static Uri CheckBaseAddress(Uri address, string paramName)
    {
        if (!address.IsAbsoluteUri)
        {
            throw new ArgumentException("The base address must be an absolute URI.", paramName);
        }

        if (HasQueryOrFragment(address))
        {
            throw new ArgumentException("The base address must not contain a query or fragment.", paramName);
        }

        if (!IsHttpScheme(address))
        {
            throw new ArgumentException("The base address must use the http or https scheme.", paramName);
        }

        return address;
    }

    /// <summary>Reads a base address from an environment variable's value, by the same rules as an option.</summary>
    /// <param name="value">The variable's value.</param>
    /// <param name="variable">The variable's name, for the messages.</param>
    /// <returns>The address, or <see langword="null"/> when the variable is unset or blank.</returns>
    /// <exception cref="InvalidOperationException">The value breaks a rule.</exception>
    public static Uri? BaseAddressFromEnvironment(string? value, string variable)
    {
        if (NonBlank(value) is not { } text)
        {
            return null;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("The " + variable + " environment variable is not an absolute URI.");
        }

        if (HasQueryOrFragment(uri))
        {
            throw new InvalidOperationException("The " + variable + " environment variable must not contain a query or fragment.");
        }

        if (!IsHttpScheme(uri))
        {
            throw new InvalidOperationException("The " + variable + " environment variable must use the http or https scheme.");
        }

        return uri;
    }

    /// <summary>Appends '/' to an address's path when it has none, so a relative request path lands below it.</summary>
    /// <param name="uri">An absolute address.</param>
    /// <returns><paramref name="uri"/>, or a new one ending in '/'.</returns>
    public static Uri WithTrailingSlash(Uri uri)
        => uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");

    /// <summary>Trims an API key set as an option and checks it has no control character, which a header cannot carry.</summary>
    /// <param name="key">The option's value, not blank.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <returns>The trimmed key.</returns>
    /// <exception cref="ArgumentException">The key contains a control character; the message does not include the key.</exception>
    public static string CheckApiKey(string key, string paramName)
    {
        var trimmed = key.Trim();
        if (HasControlCharacter(trimmed))
        {
            throw new ArgumentException("The API key contains a control character.", paramName);
        }

        return trimmed;
    }

    /// <summary>Trims an API key read from an environment variable and checks it has no control character.</summary>
    /// <param name="key">The variable's value, not blank.</param>
    /// <param name="variable">The variable's name, for the message.</param>
    /// <returns>The trimmed key.</returns>
    /// <exception cref="InvalidOperationException">The key contains a control character; the message does not include the key.</exception>
    public static string CheckEnvironmentApiKey(string key, string variable)
    {
        var trimmed = key.Trim();
        if (HasControlCharacter(trimmed))
        {
            throw new InvalidOperationException("The " + variable + " environment variable contains a control character.");
        }

        return trimmed;
    }

    /// <summary>Trims a model set as an option.</summary>
    /// <param name="model">The option's value.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <returns>The trimmed model.</returns>
    /// <exception cref="ArgumentException">The model is empty or white space.</exception>
    public static string CheckModel(string model, string paramName)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("The model must not be blank.", paramName);
        }

        return model.Trim();
    }

    /// <summary>Applies capability overrides to a provider's defaults, after checking them.</summary>
    /// <param name="defaults">The provider's defaults.</param>
    /// <param name="overrides">The options' overrides; <see langword="null"/> counts as none.</param>
    /// <param name="paramName">The parameter name the exception reports.</param>
    /// <returns>The merged capabilities.</returns>
    /// <exception cref="ArgumentException">An override allows no question kind or names an unknown one, or a limit is too low.</exception>
    public static CapabilitySet ResolveCapabilities(CapabilitySet defaults, DecisionCapabilities? overrides, string paramName)
    {
        if (overrides is null)
        {
            return defaults;
        }

        if (overrides.QuestionKinds is { } kinds)
        {
            if (kinds == QuestionKinds.None)
            {
                throw new ArgumentException("Capabilities.QuestionKinds must allow at least one question kind.", paramName);
            }

            if ((kinds & ~QuestionKinds.All) != QuestionKinds.None)
            {
                throw new ArgumentException("Capabilities.QuestionKinds has an unknown flag.", paramName);
            }
        }

        if (overrides.MaxQuestions is < 1)
        {
            throw new ArgumentException("Capabilities.MaxQuestions must be at least 1.", paramName);
        }

        if (overrides.MaxChoiceOptions is < 2)
        {
            throw new ArgumentException("Capabilities.MaxChoiceOptions must be at least 2.", paramName);
        }

        return defaults.With(overrides);
    }

    /// <summary>Returns <paramref name="value"/>, or <see langword="null"/> when it is unset, empty or white space.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool HasControlCharacter(string value)
    {
        foreach (var c in value)
        {
            if (char.IsControl(c))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasQueryOrFragment(Uri uri) => uri.Query.Length > 0 || uri.Fragment.Length > 0;

    private static bool IsHttpScheme(Uri uri)
        => string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            || string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
}
