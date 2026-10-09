using Microsoft.CodeAnalysis;

namespace Minos.AotSmoke.Tests;

/// <summary>
/// Prints a method or constructor as its PublicAPI line, the way RS0016 does: Microsoft.CodeAnalysis.PublicApiAnalyzers'
/// display format with nullability, followed by <c> -&gt; </c> and the return type. A test checks that it reproduces
/// every entry point in the PublicAPI files, so a difference shows up as a failure, never as a silent mismatch.
/// </summary>
internal static class ApiSignature
{
    private static readonly SymbolDisplayFormat Format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeParameters
            | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface
            | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeExtensionThis
            | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType
            | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        kindOptions: SymbolDisplayKindOptions.None,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.IncludeNotNullableReferenceTypeModifier);

    /// <summary>The PublicAPI line of <paramref name="method"/>'s definition: an extension call's static form, and a
    /// generic method or a member of a generic type with its type parameters, not the arguments of one call.</summary>
    public static string Of(IMethodSymbol method)
    {
        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        return definition.ToDisplayString(Format) + " -> " + definition.ReturnType.ToDisplayString(Format);
    }
}
