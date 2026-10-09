using Microsoft.Extensions.Options;
using OptionsDefaults = Microsoft.Extensions.Options.Options;

namespace Minos.DependencyInjection;

/// <summary>
/// Validates one <c>AddDecisionClient</c> registration's <see cref="DecisionClientOptions"/> through
/// <see cref="DecisionClientOptions.Validate()"/>, the check the client's constructor runs, so startup and construction apply
/// one rule set. Options of any other name are skipped.
/// </summary>
/// <param name="optionsName">The registration's options name: the empty default name, or a keyed client's key.</param>
internal sealed class DecisionClientOptionsValidator(string optionsName) : IValidateOptions<DecisionClientOptions>
{
    public ValidateOptionsResult Validate(string? name, DecisionClientOptions options)
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
