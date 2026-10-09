using System.Text.Json;
using ZeroAlloc.Validation;

namespace Minos.Validation;

/// <summary>
/// Fails text that is empty or whitespace and JSON that is exactly <c>{}</c> or <c>[]</c> (MIN003); <see langword="null"/>
/// passes, as the API accepts an absent description.
/// </summary>
internal sealed class NotBlankContentAttribute : ValidationAttribute<DecisionContent?>
{
    public override bool IsValid(DecisionContent? value) => value is not { } content || !IsBlank(content);

    /// <summary>Whether <paramref name="content"/> is blank text or an empty JSON object or array.</summary>
    public static bool IsBlank(DecisionContent content)
    {
        if (content.TryGetString(out var text))
        {
            return string.IsNullOrWhiteSpace(text);
        }

        return content.TryGetJson(out var json) && json.ValueKind switch
        {
            JsonValueKind.Object => json.GetPropertyCount() == 0,
            JsonValueKind.Array => json.GetArrayLength() == 0,
            _ => false,
        };
    }
}
