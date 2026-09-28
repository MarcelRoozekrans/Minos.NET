namespace ZeroAlloc.Jev.Generator;

/// <summary>
/// The ids of the rules <see cref="ModelBuilder"/> checks. Plain strings, not descriptors: the generator shares the
/// model builder but defines no rules. ZeroAlloc.Jev.Analyzers links this file and owns the descriptors.
/// </summary>
internal static class DiagnosticIds
{
    public const string UnsupportedType = "JEV101";
    public const string UnsupportedProperty = "JEV102";
    public const string AttributeTypeMismatch = "JEV103";
    public const string MissingLevel = "JEV104";
    public const string NoParameterlessConstructor = "JEV105";
    public const string DuplicateKey = "JEV106";
    public const string InvalidStateType = "JEV107";
}
