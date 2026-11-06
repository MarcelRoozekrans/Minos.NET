using Minos.Samples.TicketTriage;

namespace Minos.Samples.Tests;

public sealed class PricesFileTests
{
    [Fact]
    public void PricesFile_LoadsTheCommittedFile()
    {
        var table = PricesFile.Load(Path.Combine(Repository.Root, "docs", "reports", "prices.json"));

        Assert.True(table.TryFind("jev-1.14.0", out var price));
        Assert.Equal(0.042, price!.InputPerMillion);
    }

    [Fact]
    public void PricesFile_RejectsAnEmptyModel()
        => AssertRejected("""{"prices":[{"model":"","inputPerMillion":1,"outputPerMillion":1,"date":"2026-01-01","source":"s"}]}""");

    [Fact]
    public void PricesFile_RejectsANegativePrice()
        => AssertRejected("""{"prices":[{"model":"m","inputPerMillion":-1,"outputPerMillion":1,"date":"2026-01-01","source":"s"}]}""");

    private static void AssertRejected(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(path, json);
        try
        {
            var ex = Assert.Throws<InvalidDataException>(() => PricesFile.Load(path));
            Assert.Contains(path, ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
