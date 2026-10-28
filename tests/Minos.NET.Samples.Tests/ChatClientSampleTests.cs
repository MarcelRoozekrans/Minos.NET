using System.Globalization;
using Minos.Samples.ChatClient;
using ZeroAlloc.TestHelpers;

namespace Minos.Samples.Tests;

public sealed class ChatClientSampleTests
{
    [Fact]
    public async Task RunsOffline_AndShowsBothSources()
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        // The key is given empty, so a developer's own OPENROUTER_API_KEY never makes the test go online.
        var code = await ChatClientApp.RunAsync([], apiKey: string.Empty, output, error, CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Empty(error.ToString());
        Assert.Contains("Logprobs", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Verbalized", output.ToString(), StringComparison.Ordinal);
        TextSnapshot.VerifyText(output.ToString());
    }
}
