namespace Minos.PackTests;

/// <summary>A pack that fails leaves no temp directory behind: the constructor throws, so no one can dispose it.</summary>
public sealed class PackedProjectTests : IDisposable
{
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("minos-packed-project-");

    [Fact]
    public void A_pack_that_fails_deletes_its_temp_directory()
    {
        // No such project under src, so dotnet pack fails at once, after the temp directory was created.
        Assert.Throws<InvalidOperationException>(() => new PackedProject(_root, "Minos.NET.NoSuchProject"));

        Assert.Empty(_root.EnumerateFileSystemInfos());
    }

    public void Dispose() => _root.Delete(recursive: true);
}
