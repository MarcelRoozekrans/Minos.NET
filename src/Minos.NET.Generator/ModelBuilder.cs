using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Minos.Generator;

/// <summary>Turns a <c>[Questions]</c> type symbol into a cacheable model and its diagnostics.</summary>
internal static class ModelBuilder
{
    private const string NoulAttribute = "Minos.NoulAttribute";
    private const string ChoiceAttribute = "Minos.ChoiceAttribute";
    private const string ScoreAttribute = "Minos.ScoreAttribute";
    private const string CriteriaAttribute = "Minos.CriteriaAttribute";
    private const string LevelAttribute = "Minos.LevelAttribute";
    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";
    private const string JsonPropertyNameAttribute = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string StateArgument = "State";

    // The generator emits these two static members onto every question set: a question property carrying
    // either name would collide with the generated declaration.
    private static readonly string[] ReservedPropertyNames = ["Parse", "QuestionsUtf8"];

    // MIN003's second message argument: the advice that fits what is empty. The text follows the subject in the message.
    private const string EmptyTextAdvice = "is empty or whitespace: write the text, or pass null to send none";
    private const string EmptyEntryAdvice = "is empty or whitespace: write the text, or remove the entry";

    // A stub repeats the property's declared type, nullable reference annotation included, so it matches the definition.
    private static readonly SymbolDisplayFormat StubTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Builds the model for <paramref name="type"/> from its symbols alone, so the generator and the analyzer
    /// share one copy of the rules. Any syntax needed, such as the <c>State</c> argument's location, is reached
    /// through the symbols' syntax references.
    /// </summary>
    /// <param name="type">The type carrying <c>[Questions]</c>.</param>
    /// <param name="attribute">The <c>[Questions]</c> application on <paramref name="type"/>.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    public static QuestionSetResult Build(INamedTypeSymbol type, AttributeData attribute, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (!IsSupportedType(type, cancellationToken))
        {
            // A partial part can still complete most unsupported types, such as a nested, generic or abstract one, so
            // those get their stubs too; StubDeclaration says which cannot.
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.UnsupportedType, type, type.Name));
            return Result(null, BuildInvalidSet(type, cancellationToken), diagnostics);
        }

        CheckParameterlessConstructor(type, diagnostics);

        var stateType = BuildState(attribute, type, diagnostics, cancellationToken);
        var stateMembers = stateType is null ? null : StateMembers.For(stateType);

        // The rules about an enum declared in this assembly go on the enum, where ValidateEnum reports them from the
        // enum's own analysis; they still decide the set's validity here. An enum from another assembly has no
        // declaration to report on, so its findings join the set's own, on the property that uses it.
        var enumDiagnostics = new List<DiagnosticInfo>();
        var questions = new List<QuestionModel>();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IPropertySymbol property
                && BuildQuestion(property, stateMembers, diagnostics, enumDiagnostics, cancellationToken) is { } question)
            {
                questions.Add(question);
            }
        }

        ReportDuplicates(questions.Select(question => question.Key), type, type.Name, diagnostics);

        // Advice (MIN003–006) leaves the set valid; any other diagnostic makes it invalid, and the generator then
        // emits only throwing stubs for its question properties.
        if (diagnostics.Concat(enumDiagnostics).Any(diagnostic => !DiagnosticIds.IsAdvisory(diagnostic.Id)))
        {
            return Result(null, BuildInvalidSet(type, cancellationToken), diagnostics);
        }

        var model = new QuestionSetModel(
            Namespace(type.ContainingNamespace),
            Identifier(type.Name),
            type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            type.IsRecord,
            stateType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            new EquatableArray<QuestionModel>(questions.ToArray()));
        return Result(model, null, diagnostics);
    }

    private static QuestionSetResult Result(QuestionSetModel? model, InvalidSetModel? invalidSet, List<DiagnosticInfo> diagnostics)
        => new(model, invalidSet, new EquatableArray<DiagnosticInfo>(diagnostics.ToArray()));

    /// <summary>
    /// The rules about one enum used as a Choice or a Score: MIN001 or MIN002 when it is empty, MIN005 for a count
    /// outside the guidance, MIN006 or MIN104 for a member without its description, and MIN003 for an empty one. They
    /// are located on the enum, its members and their attributes, so the enum's own analysis reports them. The same
    /// code checks them inside <see cref="Build"/>, where they decide whether the set is valid.
    /// </summary>
    /// <param name="enumType">An enum declared in source.</param>
    /// <param name="kind"><see cref="QuestionKind.Choice"/> or <see cref="QuestionKind.Score"/>: how a set uses it.</param>
    /// <param name="cancellationToken">Cancels the syntax lookups.</param>
    public static EquatableArray<DiagnosticInfo> ValidateEnum(
        INamedTypeSymbol enumType, QuestionKind kind, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();
        BuildOptions(enumType, kind, externalUser: null, diagnostics, cancellationToken);
        return new EquatableArray<DiagnosticInfo>(diagnostics.ToArray());
    }

    /// <summary>
    /// The enums <paramref name="type"/>'s questions use, each with how it is used: exactly the enums whose options
    /// <see cref="Build"/> checks, so an enum's own analysis reports what a build of the set finds. An unsupported
    /// type, and a property that is not a well-formed Choice or Score question, contribute nothing.
    /// </summary>
    public static IEnumerable<(INamedTypeSymbol EnumType, QuestionKind Kind)> EnumUsages(
        INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (!IsSupportedType(type, cancellationToken))
        {
            yield break;
        }

        var ignored = new List<DiagnosticInfo>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Classify(property, ignored, cancellationToken) is { EnumType: { } enumType } question)
            {
                yield return (enumType, question.Kind);
            }
        }
    }

    /// <summary>
    /// The stubs for an invalid set: one for each question property that is an unimplemented partial definition,
    /// whatever its shape. CS9248, an unimplemented partial property, is a declaration error, and csc skips every
    /// analyzer in the compilation after one, so a single missing stub would hide all the JEV errors.
    /// </summary>
    /// <remarks>
    /// Nothing is stubbed for a property that is not a partial definition, which needs no implementation; for one the
    /// user already implemented; or for one with no question attribute, which is not the generator's to implement.
    /// Nothing is stubbed either when no partial part can reach the type: see <see cref="StubDeclaration"/>.
    /// <see langword="null"/> when no property needs a stub.
    /// </remarks>
    private static InvalidSetModel? BuildInvalidSet(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
        if (StubDeclaration(type, isSet: true, cancellationToken) is not { } setDeclaration)
        {
            return null;
        }

        // Every containing type is repeated around the set, outermost first, so each must accept a partial part too.
        var containingTypes = new List<StubTypeModel>();
        for (var containing = type.ContainingType; containing is not null; containing = containing.ContainingType)
        {
            if (StubDeclaration(containing, isSet: false, cancellationToken) is not { } containingDeclaration)
            {
                return null;
            }

            containingTypes.Insert(0, containingDeclaration);
        }

        var stubs = new List<StubPropertyModel>();
        foreach (var property in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (property is { IsPartialDefinition: true, PartialImplementationPart: null }
                && IsQuestion(property)
                && property.DeclaringSyntaxReferences
                    .Select(reference => reference.GetSyntax(cancellationToken))
                    .OfType<PropertyDeclarationSyntax>()
                    .FirstOrDefault() is { AccessorList: { } accessorList } declaration)
            {
                // The implementation repeats the definition's modifiers and accessors as written, in order, so it
                // matches whatever the definition declares, including shapes MIN102 rejects.
                stubs.Add(new StubPropertyModel(
                    string.Join(" ", declaration.Modifiers.Select(modifier => modifier.Text)),
                    property.Type.ToDisplayString(StubTypeFormat),
                    Identifier(property.Name),
                    new EquatableArray<StubAccessorModel>(accessorList.Accessors
                        .Select(accessor => new StubAccessorModel(
                            string.Join(" ", accessor.Modifiers.Select(modifier => modifier.Text)),
                            accessor.Keyword.Text))
                        .ToArray())));
            }
        }

        return stubs.Count == 0
            ? null
            : new InvalidSetModel(
                Namespace(type.ContainingNamespace),
                new EquatableArray<StubTypeModel>(containingTypes.ToArray()),
                setDeclaration,
                new EquatableArray<StubPropertyModel>(stubs.ToArray()));
    }

    /// <summary>
    /// The declaration a partial part repeats for <paramref name="type"/>, the set or a type containing it: its kind,
    /// its name and its type parameters with their variance, which every part must repeat. Constraints are left out,
    /// since a part may omit them, and so are modifiers such as <see langword="abstract"/>, <see langword="static"/>,
    /// <see langword="sealed"/>, <see langword="readonly"/> and <see langword="ref"/>, which one part declares for all.
    /// </summary>
    /// <param name="type">The set, or one of the types containing it.</param>
    /// <param name="isSet">
    /// Whether <paramref name="type"/> is the set. A containing type repeats its accessibility modifiers as declared;
    /// the set repeats none, like the valid emitter's declaration part.
    /// </param>
    /// <param name="cancellationToken">Cancels the syntax lookups.</param>
    /// <returns>
    /// <see langword="null"/> when no partial part can complete <paramref name="type"/>: it is not declared
    /// <see langword="partial"/>; it is file-local, so only its own file can reach it; or it is not a class, record,
    /// struct or interface.
    /// </returns>
    private static StubTypeModel? StubDeclaration(INamedTypeSymbol type, bool isSet, CancellationToken cancellationToken)
    {
        var keyword = type.TypeKind switch
        {
            TypeKind.Class => type.IsRecord ? "record" : "class",
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => null,
        };
        var declarations = type.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<TypeDeclarationSyntax>()
            .ToList();
        if (keyword is null
            || type.IsFileLocal
            || !declarations.Any(declaration => declaration.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            return null;
        }

        var modifiers = isSet
            ? string.Empty
            : declarations
                .Select(declaration => string.Join(
                    " ", declaration.Modifiers.Where(IsAccessibilityModifier).Select(modifier => modifier.Text)))
                .FirstOrDefault(accessibility => accessibility.Length != 0) ?? string.Empty;
        var typeParameters = type.TypeParameters.Length == 0
            ? string.Empty
            : "<" + string.Join(", ", type.TypeParameters.Select(parameter => VarianceModifier(parameter.Variance)
                + Identifier(parameter.Name))) + ">";
        return new StubTypeModel(modifiers, keyword, Identifier(type.Name), typeParameters, type.Arity);
    }

    private static string VarianceModifier(VarianceKind variance) => variance switch
    {
        VarianceKind.In => "in ",
        VarianceKind.Out => "out ",
        _ => string.Empty,
    };

    private static bool IsQuestion(IPropertySymbol property)
        => property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
            is NoulAttribute or ChoiceAttribute or ScoreAttribute);

    /// <summary>Whether the property has the shape MIN102 requires of a question: a partial, get-only instance
    /// property that is not <see langword="virtual"/>, <see langword="override"/>, <see langword="sealed"/> or
    /// <see langword="new"/>. Its name and type are checked separately.</summary>
    private static bool HasSupportedShape(IPropertySymbol property, CancellationToken cancellationToken)
        => property.IsPartialDefinition
            && !property.IsStatic
            && property.GetMethod is not null
            && property.SetMethod is null
            && !property.IsVirtual
            && !property.IsOverride
            && !property.IsSealed
            && !HasNewModifier(property, cancellationToken);

    /// <summary>Reads and validates the <c>[Questions(State = ...)]</c> named argument, if present.</summary>
    /// <returns>The state type, or <see langword="null"/> when there is none or it is invalid.</returns>
    private static ITypeSymbol? BuildState(
        AttributeData attribute,
        INamedTypeSymbol type,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != StateArgument || argument.Value.Value is not ITypeSymbol stateType)
            {
                continue;
            }

            if (!IsValidStateType(stateType))
            {
                // StateArgumentLocation returns null only when the attribute application has no source syntax
                // at all, which is not reachable from a [Questions]-decorated declaration (always in source).
                // The type's own location is the best defensive fallback then; it is not the attribute's location.
                var location = StateArgumentLocation(attribute, cancellationToken) ?? DiagnosticInfo.SourceLocation(type);
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.InvalidStateType, location, stateType.ToDisplayString()));
                return null;
            }

            return stateType;
        }

        return null;
    }

    // A static class has no value, so no instance of it could ever flow through EvaluateAsync<T, TState>: rejected.
    // An abstract class is accepted: a derived instance is a legitimate polymorphic state when its JsonTypeInfo
    // handles the derived types. An array is accepted too, provided its element type is itself valid: a JSON
    // array is a legitimate state shape, and the generated set implements IQuestionSet<TSelf, TElement[]>.
    private static bool IsValidStateType(ITypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Array)
        {
            return type is IArrayTypeSymbol arrayType && IsValidStateType(arrayType.ElementType);
        }

        return type.SpecialType != SpecialType.System_Void
            && type.TypeKind is TypeKind.Class or TypeKind.Struct
            && !type.IsStatic
            && type is not INamedTypeSymbol { IsUnboundGenericType: true };
    }

    /// <summary>
    /// The <c>State = ...</c> argument's syntax location, or <see langword="null"/> when the attribute application
    /// has no source syntax to point at (the caller falls back to the type's location).
    /// </summary>
    private static Location? StateArgumentLocation(AttributeData attribute, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax)
        {
            return null;
        }

        var argument = syntax.ArgumentList?.Arguments
            .FirstOrDefault(a => a.NameEquals?.Name.Identifier.Text == StateArgument);

        return argument is null ? syntax.GetLocation() : argument.GetLocation();
    }

    /// <summary>
    /// Checks that <c>new T()</c> can create <paramref name="type"/>: it needs a constructor invocable with no
    /// arguments, and, when it has a <see langword="required"/> member, that constructor must carry
    /// <c>[SetsRequiredMembers]</c> or the required member would still need to be set explicitly.
    /// </summary>
    private static void CheckParameterlessConstructor(INamedTypeSymbol type, List<DiagnosticInfo> diagnostics)
    {
        var invocableConstructors = type.InstanceConstructors
            .Where(constructor => constructor.Parameters.All(parameter => parameter.IsOptional))
            .ToList();

        if (invocableConstructors.Count == 0)
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.NoParameterlessConstructor, type, type.Name, string.Empty));
            return;
        }

        if (FindRequiredMember(type) is { } requiredMember
            && !invocableConstructors.Any(HasSetsRequiredMembers))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticIds.NoParameterlessConstructor,
                type,
                type.Name,
                $"; required member '{requiredMember.Name}' has no default value"));
        }
    }

    /// <summary>Finds a <see langword="required"/> member on <paramref name="type"/> or any base type: a
    /// constructor's <c>[SetsRequiredMembers]</c> covers inherited required members too, so the search must too.</summary>
    private static ISymbol? FindRequiredMember(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var found = current.GetMembers().FirstOrDefault(candidate => candidate switch
            {
                IPropertySymbol property => property.IsRequired,
                IFieldSymbol field => field.IsRequired,
                _ => false,
            });

            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static bool HasSetsRequiredMembers(IMethodSymbol constructor)
        => constructor.GetAttributes()
            .Any(attribute => attribute.AttributeClass?.ToDisplayString() == SetsRequiredMembersAttribute);

    private static bool IsSupportedType(INamedTypeSymbol type, CancellationToken cancellationToken)
        => type.TypeKind == TypeKind.Class
            && type.ContainingType is null
            && !type.IsGenericType
            && !type.IsAbstract
            && !type.IsStatic
            && !type.IsFileLocal
            && type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
                && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    /// <param name="property">A member of the set.</param>
    /// <param name="stateMembers">The <c>State</c> type's members, for MIN004; <see langword="null"/> without one.</param>
    /// <param name="diagnostics">The set's own findings.</param>
    /// <param name="enumDiagnostics">The findings about an enum declared in this assembly, which the enum reports.</param>
    /// <param name="cancellationToken">Cancels the syntax lookups.</param>
    private static QuestionModel? BuildQuestion(
        IPropertySymbol property,
        StateMembers? stateMembers,
        List<DiagnosticInfo> diagnostics,
        List<DiagnosticInfo> enumDiagnostics,
        CancellationToken cancellationToken)
    {
        if (Classify(property, diagnostics, cancellationToken) is not { } question)
        {
            return null;
        }

        var (attribute, kind, enumType) = question;
        var what = $"The instruction text of '{property.Name}'";

        var options = new EquatableArray<OptionModel>(Array.Empty<OptionModel>());
        if (enumType is not null)
        {
            var declaredHere = SymbolEqualityComparer.Default.Equals(enumType.ContainingAssembly, property.ContainingAssembly);
            options = BuildOptions(
                enumType,
                kind,
                declaredHere ? null : property,
                declaredHere ? enumDiagnostics : diagnostics,
                cancellationToken);
            ReportDuplicates(options.Select(option => option.Key), property, property.Name, diagnostics);
        }

        var instructions = TextFragment(Positional(attribute));
        CheckText(attribute, what, property, diagnostics, cancellationToken);
        stateMembers?.CheckReferences(attribute, property, diagnostics, cancellationToken);

        return new QuestionModel(
            Identifier(property.Name),
            Modifiers(property, cancellationToken),
            kind,
            Named(attribute, "Key") ?? SnakeCase.Convert(property.Name),
            instructions,
            Named(attribute, "WhenTrue"),
            Named(attribute, "WhenFalse"),
            enumType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            options);
    }

    /// <summary>A property's question attribute, its kind and, for a Choice or a Score, its enum.</summary>
    private sealed record Question(AttributeData Attribute, QuestionKind Kind, INamedTypeSymbol? EnumType);

    /// <summary>
    /// Whether <paramref name="property"/> is a well-formed question: one question attribute, a supported shape and
    /// name, and the type its attribute calls for. MIN102 and MIN103 go to <paramref name="diagnostics"/> when not.
    /// </summary>
    /// <returns>The question, or <see langword="null"/> when the property is no question or a malformed one.</returns>
    private static Question? Classify(
        IPropertySymbol property,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        AttributeData? attribute = null;
        var kind = QuestionKind.Noul;
        var foundKinds = new List<QuestionKind>();
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
                foundKinds.Add(found);
            }
        }

        if (attribute is null)
        {
            return null;
        }

        if (!HasSupportedShape(property, cancellationToken)
            || ReservedPropertyNames.Contains(property.Name, StringComparer.Ordinal))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.UnsupportedProperty, property, property.Name));
            return null;
        }

        var enumType = QuestionEnum(property.Type, kind);
        var typeMatches = kind == QuestionKind.Noul
            ? property.Type.ToDisplayString() == "Minos.Noul"
            : enumType is not null;
        if (foundKinds.Count != 1 || !typeMatches)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticIds.AttributeTypeMismatch,
                property,
                property.Name,
                string.Join(", ", foundKinds.Select(AttributeName))));
            return null;
        }

        return new Question(attribute, kind, enumType);
    }

    private static INamedTypeSymbol? QuestionEnum(ITypeSymbol type, QuestionKind kind)
    {
        var expected = kind switch
        {
            QuestionKind.Choice => "Minos.Choice<T>",
            QuestionKind.Score => "Minos.Score<T>",
            _ => null,
        };

        return expected is not null
            && type is INamedTypeSymbol { IsGenericType: true } named
            && named.OriginalDefinition.ToDisplayString() == expected
            && named.TypeArguments[0] is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
                ? enumType
                : null;
    }

    /// <summary>The options of <paramref name="enumType"/> in wire order, checking the enum rules on the way.</summary>
    /// <param name="enumType">The Choice or Score enum.</param>
    /// <param name="kind">How the enum is used.</param>
    /// <param name="externalUser">
    /// The property using the enum when the enum is declared in another assembly, which has no declaration here to
    /// report on: every finding then lands on the property. <see langword="null"/> for an enum declared in this
    /// assembly, whose findings land on the enum, its members and their attributes.
    /// </param>
    /// <param name="diagnostics">The list the findings go to.</param>
    /// <param name="cancellationToken">Cancels the syntax lookups.</param>
    private static EquatableArray<OptionModel> BuildOptions(
        INamedTypeSymbol enumType,
        QuestionKind kind,
        IPropertySymbol? externalUser,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var options = new List<OptionModel>();
        var seenValues = new List<object?>();

        ISymbol At(ISymbol symbol) => externalUser ?? symbol;

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
                var what = $"The [Criteria] description of '{enumType.Name}.{field.Name}'";
                string? descriptionFragment = null;
                if (criteria is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.MissingCriteria, At(field), enumType.Name, field.Name));
                }
                else
                {
                    CheckText(criteria, what, At(field), diagnostics, cancellationToken);
                    CheckEntries(criteria, "Examples", what, At(field), diagnostics, cancellationToken);
                    CheckEntries(criteria, "NotFor", what, At(field), diagnostics, cancellationToken);
                    descriptionFragment = DescriptionFragment(criteria);
                }

                options.Add(new OptionModel(
                    Identifier(field.Name),
                    (criteria is null ? null : Named(criteria, "Key")) ?? SnakeCase.Convert(field.Name),
                    descriptionFragment));
            }
            else if (Find(field, LevelAttribute) is { } level)
            {
                var what = $"The [Level] description of '{enumType.Name}.{field.Name}'";
                CheckText(level, what, At(field), diagnostics, cancellationToken);
                CheckEntries(level, "Examples", what, At(field), diagnostics, cancellationToken);
                CheckEntries(level, "NotFor", what, At(field), diagnostics, cancellationToken);
                options.Add(new OptionModel(
                    Identifier(field.Name),
                    options.Count.ToString(CultureInfo.InvariantCulture),
                    DescriptionFragment(level)));
            }
            else
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.MissingLevel, At(field), enumType.Name, field.Name));
            }
        }

        CheckMemberCount(enumType, kind, seenValues.Count, At(enumType), diagnostics);
        return new EquatableArray<OptionModel>(options.ToArray());
    }

    /// <summary>MIN001 and MIN002 for an enum with no members, which the API rejects; MIN005 for a count outside the
    /// API sketch's guidance. Aliases are not counted: they repeat a member already on the wire.</summary>
    private static void CheckMemberCount(
        INamedTypeSymbol enumType,
        QuestionKind kind,
        int count,
        ISymbol location,
        List<DiagnosticInfo> diagnostics)
    {
        var isScore = kind == QuestionKind.Score;
        if (count < DecisionLimits.MinimumOptions)
        {
            var id = isScore ? DiagnosticIds.EmptyScoreEnum : DiagnosticIds.EmptyChoiceEnum;
            diagnostics.Add(DiagnosticInfo.Create(id, location, enumType.Name));
        }
        else if (isScore
            ? count is < DecisionLimits.MinimumScoreLevels or > DecisionLimits.MaximumScoreLevels
            : count > DecisionLimits.MaximumChoiceOptions)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticIds.OptionCountOutsideGuidance,
                location,
                isScore ? "Score" : "Choice",
                enumType.Name,
                count.ToString(CultureInfo.InvariantCulture),
                isScore
                    ? $"{DecisionLimits.MinimumScoreLevels} to {DecisionLimits.MaximumScoreLevels} levels"
                    : $"at most {DecisionLimits.MaximumChoiceOptions} options"));
        }
    }

    /// <summary>
    /// MIN003: a text argument that is an empty or whitespace string. Null is fine: the API accepts it.
    /// </summary>
    /// <param name="attribute">The attribute whose first constructor argument is the text.</param>
    /// <param name="what">What the text is, for the message: "The instruction text of 'Answer'", for one.</param>
    /// <param name="fallback">Where to report when the attribute has no source syntax, as on an enum from metadata.</param>
    /// <param name="diagnostics">The list to add the diagnostic to.</param>
    /// <param name="cancellationToken">Cancels the syntax lookup.</param>
    private static void CheckText(
        AttributeData attribute,
        string what,
        ISymbol fallback,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        if (attribute.ConstructorArguments.Length > 0
            && attribute.ConstructorArguments[0].Value is string text
            && string.IsNullOrWhiteSpace(text))
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticIds.EmptyText, TextArgumentLocation(attribute, fallback, cancellationToken), what, EmptyTextAdvice));
        }
    }

    /// <summary>
    /// MIN003: an entry of an array-valued named argument, such as <c>Examples</c> or <c>NotFor</c>, that is
    /// <see langword="null"/> or a whitespace string. Reported once at the named argument's location, however many
    /// entries are blank.
    /// </summary>
    /// <param name="attribute">The attribute carrying the named argument.</param>
    /// <param name="name">The named argument's name, such as <c>"Examples"</c>.</param>
    /// <param name="what">What the owning text is, lowered and folded into the message.</param>
    /// <param name="fallback">Where to report when the attribute has no source syntax.</param>
    /// <param name="diagnostics">The list to add the diagnostic to.</param>
    /// <param name="cancellationToken">Cancels the syntax lookup.</param>
    private static void CheckEntries(
        AttributeData attribute,
        string name,
        string what,
        ISymbol fallback,
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key != name || argument.Value.Kind != TypedConstantKind.Array || argument.Value.IsNull)
            {
                continue;
            }

            if (argument.Value.Values.Any(value => value.Value is not string text || string.IsNullOrWhiteSpace(text)))
            {
                var whatLowerFirst = what.Length == 0 ? what : char.ToLowerInvariant(what[0]) + what.Substring(1);
                diagnostics.Add(DiagnosticInfo.Create(
                    DiagnosticIds.EmptyText,
                    NamedArgumentLocation(attribute, name, fallback, cancellationToken),
                    $"An entry of {name} on {whatLowerFirst}",
                    EmptyEntryAdvice));
            }

            return;
        }
    }

    /// <summary>The attribute's positional text argument, else the attribute, else <paramref name="fallback"/>'s location.</summary>
    private static Location? TextArgumentLocation(AttributeData attribute, ISymbol fallback, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax)
        {
            return DiagnosticInfo.SourceLocation(fallback);
        }

        var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals is null);
        return argument is null ? syntax.GetLocation() : argument.GetLocation();
    }

    /// <summary>The named argument's syntax, else the attribute, else <paramref name="fallback"/>'s location.</summary>
    private static Location? NamedArgumentLocation(
        AttributeData attribute, string name, ISymbol fallback, CancellationToken cancellationToken)
    {
        if (attribute.ApplicationSyntaxReference?.GetSyntax(cancellationToken) is not AttributeSyntax syntax)
        {
            return DiagnosticInfo.SourceLocation(fallback);
        }

        var argument = syntax.ArgumentList?.Arguments.FirstOrDefault(a => a.NameEquals?.Name.Identifier.ValueText == name);
        return argument is null ? syntax.GetLocation() : argument.GetLocation();
    }

    private static void ReportDuplicates(
        IEnumerable<string> keys,
        ISymbol location,
        string owner,
        List<DiagnosticInfo> diagnostics)
    {
        foreach (var group in keys.GroupBy(key => key, StringComparer.Ordinal).Where(group => group.Count() > 1))
        {
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.DuplicateKey, location, group.Key, owner));
        }
    }

    /// <summary>Whether the property's defining declaration carries the <see langword="new"/> modifier: it hides a
    /// base member rather than overriding it, so it has no symbol-level flag of its own.</summary>
    private static bool HasNewModifier(IPropertySymbol property, CancellationToken cancellationToken)
        => property.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax(cancellationToken))
            .OfType<PropertyDeclarationSyntax>()
            .Any(declaration => declaration.Modifiers.Any(SyntaxKind.NewKeyword));

    private static AttributeData? Find(ISymbol symbol, string attributeName)
        => symbol.GetAttributes().FirstOrDefault(attribute => attribute.AttributeClass?.ToDisplayString() == attributeName);

    private static string Positional(AttributeData attribute)
        => attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string value
            ? value
            : string.Empty;

    /// <summary>A plain text as its wire fragment, a JSON string.</summary>
    private static string TextFragment(string text) => new StringBuilder().AppendJsonString(text).ToString();

    /// <summary>
    /// A <c>[Criteria]</c> or <c>[Level]</c> description as its wire fragment: the plain text as a JSON string, or a
    /// criterion object when <c>Examples</c> or <c>NotFor</c> holds a text. <see langword="null"/> entries are left out.
    /// </summary>
    private static string DescriptionFragment(AttributeData attribute)
    {
        var examples = NamedStrings(attribute, "Examples");
        var notFor = NamedStrings(attribute, "NotFor");
        var description = Positional(attribute);
        if (examples.Length == 0 && notFor.Length == 0)
        {
            return TextFragment(description);
        }

        var json = new StringBuilder("{\"description\":").AppendJsonString(description);
        AppendArray(json, "examples", examples);
        AppendArray(json, "not_for", notFor);
        return json.Append('}').ToString();

        static void AppendArray(StringBuilder json, string name, string[] values)
        {
            if (values.Length == 0)
            {
                return;
            }

            json.Append(",\"").Append(name).Append("\":[");
            for (var i = 0; i < values.Length; i++)
            {
                json.Append(i > 0 ? "," : string.Empty).AppendJsonString(values[i]);
            }

            json.Append(']');
        }
    }

    /// <summary>The non-null strings of an array-valued named argument; empty when it is absent or <see langword="null"/>.</summary>
    private static string[] NamedStrings(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (argument.Key == name && argument.Value.Kind == TypedConstantKind.Array && !argument.Value.IsNull)
            {
                return argument.Value.Values.Select(value => value.Value as string).OfType<string>().ToArray();
            }
        }

        return [];
    }

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

        return string.Join(" ", declaration.Modifiers.Where(IsAccessibilityModifier).Select(modifier => modifier.Text));
    }

    private static bool IsAccessibilityModifier(SyntaxToken modifier)
        => modifier.IsKind(SyntaxKind.PublicKeyword)
            || modifier.IsKind(SyntaxKind.InternalKeyword)
            || modifier.IsKind(SyntaxKind.ProtectedKeyword)
            || modifier.IsKind(SyntaxKind.PrivateKeyword);

    private static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;

    /// <summary>Builds a dotted namespace name with each segment individually escaped as a C# identifier.</summary>
    private static string? Namespace(INamespaceSymbol ns)
    {
        if (ns.IsGlobalNamespace)
        {
            return null;
        }

        var parent = Namespace(ns.ContainingNamespace);
        var segment = Identifier(ns.Name);
        return parent is null ? segment : parent + "." + segment;
    }

    private static string AttributeName(QuestionKind kind) => kind switch
    {
        QuestionKind.Noul => "Noul",
        QuestionKind.Choice => "Choice",
        _ => "Score",
    };

    /// <summary>
    /// MIN004: the names a backticked token in instructions may use for a public instance property or field of the
    /// <c>State</c> type, inherited ones included. Matching is case-insensitive, so the camelCase form of a name
    /// needs no entry of its own.
    /// </summary>
    private sealed class StateMembers
    {
        private readonly string displayName;
        private readonly HashSet<string> names;

        private StateMembers(string displayName, HashSet<string> names)
        {
            this.displayName = displayName;
            this.names = names;
        }

        /// <summary>
        /// The members of <paramref name="stateType"/>, or of its element type for an array state: instructions over
        /// a JSON array refer to the fields of its items. <see langword="null"/> when there is no named type to check.
        /// </summary>
        public static StateMembers? For(ITypeSymbol stateType)
        {
            var itemType = stateType;
            while (itemType is IArrayTypeSymbol array)
            {
                itemType = array.ElementType;
            }

            if (itemType is not INamedTypeSymbol named)
            {
                return null;
            }

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var current = named; current is not null; current = current.BaseType)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member.IsStatic
                        || member.IsImplicitlyDeclared
                        || member.DeclaredAccessibility != Accessibility.Public
                        || member is not (IFieldSymbol or IPropertySymbol { IsIndexer: false }))
                    {
                        continue;
                    }

                    var snakeCase = SnakeCase.Convert(member.Name);
                    names.Add(member.Name);
                    names.Add(snakeCase);
                    names.Add(snakeCase.Replace('_', '-'));
                    if (Find(member, JsonPropertyNameAttribute) is { } jsonName && Positional(jsonName) is { Length: > 0 } name)
                    {
                        names.Add(name);
                    }
                }
            }

            return new StateMembers(stateType.ToDisplayString(), names);
        }

        /// <summary>
        /// Reports each backticked identifier in a question's instructions that names no state member.
        /// </summary>
        /// <param name="attribute">The question's attribute, whose first constructor argument is the plain-text instructions.</param>
        /// <param name="property">Where to report when the attribute has no source syntax.</param>
        /// <param name="diagnostics">The list to add findings to.</param>
        /// <param name="cancellationToken">Cancels the syntax lookup.</param>
        public void CheckReferences(
            AttributeData attribute,
            IPropertySymbol property,
            List<DiagnosticInfo> diagnostics,
            CancellationToken cancellationToken)
        {
            if (attribute.ConstructorArguments.Length == 0 || attribute.ConstructorArguments[0].Value is not string instructions)
            {
                return;
            }

            foreach (var token in BacktickedIdentifiers(instructions).Distinct(StringComparer.Ordinal))
            {
                if (!names.Contains(token))
                {
                    diagnostics.Add(DiagnosticInfo.Create(
                        DiagnosticIds.UnknownStateReference,
                        TextArgumentLocation(attribute, property, cancellationToken),
                        token,
                        displayName));
                }
            }
        }

        /// <summary>
        /// The text between each pair of backticks that is a single identifier: a letter or <c>_</c>, then letters,
        /// digits, <c>_</c> or <c>-</c>. Dotted paths and other text are skipped, as is an unclosed backtick.
        /// </summary>
        private static IEnumerable<string> BacktickedIdentifiers(string text)
        {
            var segments = text.Split('`');

            // Odd segments lie between a pair of backticks, except a last one, which has no closing backtick.
            for (var i = 1; i < segments.Length - 1; i += 2)
            {
                if (IsIdentifier(segments[i]))
                {
                    yield return segments[i];
                }
            }
        }

        private static bool IsIdentifier(string token)
        {
            if (token.Length == 0 || !(IsAsciiLetter(token[0]) || token[0] == '_'))
            {
                return false;
            }

            for (var i = 1; i < token.Length; i++)
            {
                var c = token[i];
                if (!(IsAsciiLetter(c) || c is (>= '0' and <= '9') or '_' or '-'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsAsciiLetter(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');
    }
}
