using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ZeroAlloc.Jev.Generator;

/// <summary>Turns a <c>[JevQuestions]</c> type symbol into a cacheable model and its diagnostics.</summary>
internal static class ModelBuilder
{
    private const string NoulAttribute = "ZeroAlloc.Jev.NoulAttribute";
    private const string ChoiceAttribute = "ZeroAlloc.Jev.ChoiceAttribute";
    private const string ScoreAttribute = "ZeroAlloc.Jev.ScoreAttribute";
    private const string CriteriaAttribute = "ZeroAlloc.Jev.CriteriaAttribute";
    private const string LevelAttribute = "ZeroAlloc.Jev.LevelAttribute";
    private const string SetsRequiredMembersAttribute = "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute";
    private const string JsonPropertyNameAttribute = "System.Text.Json.Serialization.JsonPropertyNameAttribute";
    private const string StateArgument = "State";

    // The generator emits these two static members onto every question set: a question property carrying
    // either name would collide with the generated declaration.
    private static readonly string[] ReservedPropertyNames = ["Parse", "QuestionsUtf8"];

    // The API sketch's guidance, which JEV005 warns about: the schema itself sets no upper bound.
    private const int MinimumScoreLevels = 2;
    private const int MaximumScoreLevels = 10;
    private const int MaximumChoiceOptions = 255;

    // A stub repeats the property's declared type, nullable reference annotation included, so it matches the definition.
    private static readonly SymbolDisplayFormat StubTypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
        .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    /// Builds the model for <paramref name="type"/> from its symbols alone, so the generator and the analyzer
    /// share one copy of the rules. Any syntax needed, such as the <c>State</c> argument's location, is reached
    /// through the symbols' syntax references.
    /// </summary>
    /// <param name="type">The type carrying <c>[JevQuestions]</c>.</param>
    /// <param name="attribute">The <c>[JevQuestions]</c> application on <paramref name="type"/>.</param>
    /// <param name="cancellationToken">Cancels the build.</param>
    public static QuestionSetResult Build(INamedTypeSymbol type, AttributeData attribute, CancellationToken cancellationToken)
    {
        var diagnostics = new List<DiagnosticInfo>();

        if (!IsSupportedType(type, cancellationToken))
        {
            // No stubs: a partial declaration of an unsupported type would not compile, or would not reach it.
            diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.UnsupportedType, type, type.Name));
            return Result(null, null, diagnostics);
        }

        CheckParameterlessConstructor(type, diagnostics);

        var stateType = BuildState(attribute, type, diagnostics, cancellationToken);
        var stateMembers = stateType is null ? null : StateMembers.For(stateType);

        var questions = new List<QuestionModel>();
        foreach (var member in type.GetMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is IPropertySymbol property
                && BuildQuestion(property, stateMembers, diagnostics, cancellationToken) is { } question)
            {
                questions.Add(question);
            }
        }

        ReportDuplicates(questions.Select(question => question.Key), type, type.Name, diagnostics);

        // Advice (JEV003–006) leaves the set valid; any other diagnostic makes it invalid, and the generator then
        // emits only throwing stubs for its question properties.
        if (diagnostics.Any(diagnostic => !DiagnosticIds.IsAdvisory(diagnostic.Id)))
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
    /// The stubs for an invalid set: one for each question property that is an unimplemented partial definition,
    /// whatever its shape. CS9248, an unimplemented partial property, is a declaration error, and csc skips every
    /// analyzer in the compilation after one, so a single missing stub would hide all the JEV errors.
    /// </summary>
    /// <remarks>
    /// Nothing is stubbed for a property that is not a partial definition, which needs no implementation; for one the
    /// user already implemented; or for one with no question attribute, which is not the generator's to implement.
    /// <see langword="null"/> when no property needs a stub.
    /// </remarks>
    private static InvalidSetModel? BuildInvalidSet(INamedTypeSymbol type, CancellationToken cancellationToken)
    {
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
                // matches whatever the definition declares, including shapes JEV102 rejects.
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
                Identifier(type.Name),
                type.IsRecord,
                new EquatableArray<StubPropertyModel>(stubs.ToArray()));
    }

    private static bool IsQuestion(IPropertySymbol property)
        => property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
            is NoulAttribute or ChoiceAttribute or ScoreAttribute);

    /// <summary>Whether the property has the shape JEV102 requires of a question: a partial, get-only instance
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

    /// <summary>Reads and validates the <c>[JevQuestions(State = ...)]</c> named argument, if present.</summary>
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
                // at all, which is not reachable from a [JevQuestions]-decorated declaration (always in source).
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
    // array is a legitimate state shape, and the generated set implements IJevQuestionSet<TSelf, TElement[]>.
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

    private static QuestionModel? BuildQuestion(
        IPropertySymbol property,
        StateMembers? stateMembers,
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
            ? property.Type.ToDisplayString() == "ZeroAlloc.Jev.Noul"
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

        CheckText(attribute, $"The instruction text of '{property.Name}'", property, diagnostics, cancellationToken);
        stateMembers?.CheckReferences(attribute, property, diagnostics, cancellationToken);

        var options = enumType is null
            ? new EquatableArray<OptionModel>(Array.Empty<OptionModel>())
            : BuildOptions(enumType, kind, property, diagnostics, cancellationToken);

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
            QuestionKind.Choice => "ZeroAlloc.Jev.Choice<T>",
            QuestionKind.Score => "ZeroAlloc.Jev.Score<T>",
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
        List<DiagnosticInfo> diagnostics,
        CancellationToken cancellationToken)
    {
        var options = new List<OptionModel>();
        var seenValues = new List<object?>();

        // An enum declared in another assembly has no source location, so its diagnostics go on the property.
        ISymbol At(ISymbol symbol) => symbol.Locations.Any(l => l.IsInSource) ? symbol : property;

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
                if (criteria is null)
                {
                    diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.MissingCriteria, At(field), enumType.Name, field.Name));
                }
                else
                {
                    CheckText(criteria, $"The [Criteria] description of '{enumType.Name}.{field.Name}'", property, diagnostics, cancellationToken);
                }

                options.Add(new OptionModel(
                    Identifier(field.Name),
                    (criteria is null ? null : Named(criteria, "Key")) ?? SnakeCase.Convert(field.Name),
                    criteria is null ? null : Positional(criteria)));
            }
            else if (Find(field, LevelAttribute) is { } level)
            {
                CheckText(level, $"The [Level] description of '{enumType.Name}.{field.Name}'", property, diagnostics, cancellationToken);
                options.Add(new OptionModel(
                    Identifier(field.Name),
                    options.Count.ToString(CultureInfo.InvariantCulture),
                    Positional(level)));
            }
            else
            {
                diagnostics.Add(DiagnosticInfo.Create(DiagnosticIds.MissingLevel, At(field), enumType.Name, field.Name));
            }
        }

        CheckMemberCount(enumType, kind, seenValues.Count, At(enumType), diagnostics);
        ReportDuplicates(options.Select(option => option.Key), property, property.Name, diagnostics);
        return new EquatableArray<OptionModel>(options.ToArray());
    }

    /// <summary>JEV001 and JEV002 for an enum with no members, which the API rejects; JEV005 for a count outside the
    /// API sketch's guidance. Aliases are not counted: they repeat a member already on the wire.</summary>
    private static void CheckMemberCount(
        INamedTypeSymbol enumType,
        QuestionKind kind,
        int count,
        ISymbol location,
        List<DiagnosticInfo> diagnostics)
    {
        var isScore = kind == QuestionKind.Score;
        if (count == 0)
        {
            var id = isScore ? DiagnosticIds.EmptyScoreEnum : DiagnosticIds.EmptyChoiceEnum;
            diagnostics.Add(DiagnosticInfo.Create(id, location, enumType.Name));
        }
        else if (isScore ? count is < MinimumScoreLevels or > MaximumScoreLevels : count > MaximumChoiceOptions)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                DiagnosticIds.OptionCountOutsideGuidance,
                location,
                isScore ? "Score" : "Choice",
                enumType.Name,
                count.ToString(CultureInfo.InvariantCulture),
                isScore
                    ? $"{MinimumScoreLevels} to {MaximumScoreLevels} levels"
                    : $"at most {MaximumChoiceOptions} options"));
        }
    }

    /// <summary>JEV003: a text argument that is an empty or whitespace string. Null is fine: the API accepts it.</summary>
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
                DiagnosticIds.EmptyText, TextArgumentLocation(attribute, fallback, cancellationToken), what));
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
    /// JEV004: the names a backticked token in instructions may use for a public instance property or field of the
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

        /// <summary>Reports each backticked identifier in a question's instructions that names no state member.</summary>
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
