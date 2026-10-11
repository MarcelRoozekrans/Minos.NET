using Microsoft.Extensions.Options;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Minos.SystemOne;

/// <summary>
/// Validates one <c>AddSystemOneClient</c> registration's <see cref="SystemOneClientOptions"/> through
/// <see cref="SystemOneClientOptions.Validate()"/>, the check the client's constructor runs, so startup and construction apply
/// one rule set. Options of any other name are skipped.
/// </summary>
/// <param name="optionsName">The registration's options name: the empty default name, or a keyed client's key.</param>
internal sealed class SystemOneClientOptionsValidator(string optionsName) : IValidateOptions<SystemOneClientOptions>
{
    public ValidateOptionsResult Validate(string? name, SystemOneClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(name ?? OptionsDefaults.DefaultName, optionsName, StringComparison.Ordinal))
        {
            return ValidateOptionsResult.Skip;
        }

        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (ArgumentException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
