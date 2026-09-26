using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Jev.Net.Generators;

/// <summary>Turns a <c>[JevQuestions]</c> type symbol into a cacheable model and its diagnostics.</summary>
internal static class ModelBuilder
{
    private const string NoulAttribute = "Jev.Net.NoulAttribute";
    private const string ChoiceAttribute = "Jev.Net.ChoiceAttribute";
    private const string ScoreAttribute = "Jev.Net.ScoreAttribute";
    private const string CriteriaAttribute = "Jev.Net.CriteriaAttribute";
    private const string LevelAttribute = "Jev.Net.LevelAttribute";

    public static QuestionSetResult Build(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (!IsSupportedType(type, cancellationToken))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedType, type, type.Name));
            return Result(null, diagnostics);
        }

        if (!type.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NoParameterlessConstructor, type, type.Name));
        }

        var questions = new List<QuestionModel>();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IPropertySymbol property
                && BuildQuestion(property, diagnostics, cancellationToken) is { } question)
            {
                questions.Add(question);
            }
        }

        ReportDuplicates(questions.Select(question => question.Key), type, type.Name, diagnostics);

        if (diagnostics.Count > 0)
        {
            return Result(null, diagnostics);
        }

        var model = new QuestionSetModel(
            type.ContainingNamespace.IsGlobalNamespace ? null : type.ContainingNamespace.ToDisplayString(),
            type.Name,
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.IsRecord,
            new EquatableArray<QuestionModel>(questions.ToArray()));
        return Result(model, diagnostics);
    }

    private static QuestionSetResult Result(QuestionSetModel? model, List<DiagnosticInfo> diagnostics)
        => new(model, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));

    private static bool IsSupportedType(INamedTypeSymbol type, CancellationToken cancellationToken)
        => type.TypeKind == TypeKind.Class
            && type.ContainingType is null
            && !type.IsGenericType
            && !type.IsAbstract
            && !type.IsStatic
            && type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static QuestionModel? BuildQuestion(
        IPropertySymbol property,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        AttributeData? attribute = null;
        var kind = QuestionKind.Noul;
        var attributeCount = 0;
        foreach (var candidate in property.GetAttributes())
        {
            QuestionKind? candidateKind = candidate.AttributeClass?.ToDisplayString() switch
            {
                NoulAttribute => QuestionKind.Noul,
                ChoiceAttribute => QuestionKind.Choice,
                ScoreAttribute => QuestionKind.Score,
                _ => null,
            };

            if (candidateKind is { } found)
            {
                attribute = candidate;
                kind = found;
                attributeCount++;
            }
        }

        if (attribute is null)
        {
            return null;
        }

        if (!property.IsPartialDefinition || property.IsStatic || property.GetMethod is null || property.SetMethod is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UnsupportedProperty, property, property.Name));
            return null;
        }

        var enumType = QuestionEnum(property.Type, kind);
        var typeMatches = kind == QuestionKind.Noul
            ? property.Type.ToDisplayString() == "Jev.Net.Noul"
            : enumType is not null;
        if (attributeCount != 1 || !typeMatches)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.AttributeTypeMismatch, property, property.Name, AttributeName(kind), ExpectedType(kind)));
            return null;
        }

        var options = enumType is null
            ? new EquatableArray<OptionModel>(Array.Empty<OptionModel>())
            : BuildOptions(enumType, kind, property, diagnostics);

        return new QuestionModel(
            Identifier(property.Name),
            Modifiers(property, cancellationToken),
            kind,
            Named(attribute, "Key") ?? SnakeCase.Convert(property.Name),
            Positional(attribute),
            Named(attribute, "True"),
            Named(attribute, "False"),
            enumType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            options);
    }

    private static INamedTypeSymbol? QuestionEnum(ITypeSymbol type, QuestionKind kind)
    {
        var expected = kind switch
        {
            QuestionKind.Choice => "Jev.Net.Choice<T>",
            QuestionKind.Score => "Jev.Net.Score<T>",
            _ => null,
        };

        return expected is not null
            && type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.ToDisplayString() == expected
            && named.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                ? enumType
                : null;
    }

    private static EquatableArray<OptionModel> BuildOptions(
        INamedTypeSymbol enumType,
        QuestionKind kind,
        IPropertySymbol property,
        List<DiagnosticInfo> diagnostics)
    {
        var options = new List<OptionModel>();
        var seenValues = new List<object?>();

        foreach (var field in enumType.GetMembers().OfType<IFieldSymbol>())
        {
            // An alias repeats an earlier member's value; the earlier member already represents it.
            if (!field.HasConstantValue || seenValues.Contains(field.ConstantValue))
            {
                continue;
            }

            seenValues.Add(field.ConstantValue);

            if (kind == QuestionKind.Choice)
            {
                var criteria = Find(field, CriteriaAttribute);
                options.Add(new OptionModel(
                    Identifier(field.Name),
                    (criteria is null ? null : Named(criteria, "Key")) ?? SnakeCase.Convert(field.Name),
                    criteria is null ? null : Positional(criteria)));
            }
            else if (Find(field, LevelAttribute) is { } level)
            {
                options.Add(new OptionModel(
                    Identifier(field.Name),
                    options.Count.ToString(CultureInfo.InvariantCulture),
                    Positional(level)));
            }
            else
            {
                ISymbol location = field.Locations.Any(l => l.IsInSource) ? field : property;
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.MissingLevel, location, enumType.Name, field.Name));
            }
        }

        ReportDuplicates(options.Select(option => option.Key), property, property.Name, diagnostics);
        return new EquatableArray<OptionModel>(options.ToArray());
    }

    private static void ReportDuplicates(
        IEnumerable<string> keys,
        ISymbol location,
        string owner,
        List<DiagnosticInfo> diagnostics)
    {
        foreach (var group in keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.DuplicateKey, location, group.Key, owner));
        }
    }

    private static AttributeData? Find(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);

    private static string Positional(AttributeData attribute)
        => attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string value
            ? value
            : string.Empty;

    private static string? Named(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name)
            {
                return argument.Value.Value as string;
            }
        }

        return null;
    }

    private static string Modifiers(IPropertySymbol property, CancellationToken cancellationToken)
    {
        var declaration = property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<PropertyDeclarationSyntax>()
            .First();

        return string.Join(
            " ",
            declaration.Modifiers
                .Where(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)
                    || modifier.IsKind(SyntaxKind.InternalKeyword)
                    || modifier.IsKind(SyntaxKind.ProtectedKeyword)
                    || modifier.IsKind(SyntaxKind.PrivateKeyword))
                .Select(modifier => modifier.Text));
    }

    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    private static string AttributeName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "Noul",
        QuestionKind.Choice => "Choice",
        _ => "Score",
    };

    private static string ExpectedType(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "Noul",
        QuestionKind.Choice => "Choice<TEnum>",
        _ => "Score<TEnum>",
    };
}
