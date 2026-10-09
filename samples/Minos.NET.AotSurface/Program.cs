namespace Minos.AotSurface;

/// <summary>
/// The host for the whole-surface trim and AOT check. The csproj roots both packages and this assembly, so publishing
/// it is the check; the program itself only touches the generated set so the entry point is not empty.
/// </summary>
internal static class Program
{
    private static int Main() => SurfaceTriage.QuestionsUtf8.IsEmpty ? 1 : 0;
}
