using System.Globalization;
using System.Net;
using System.Net.Sockets;
using ZeroAlloc.Jev.Benchmarks.Mock;

namespace ZeroAlloc.Jev.Benchmarks.Tests;

/// <summary>The mock's process: its exit codes and messages, and how it stops.</summary>
public sealed class MockProgramTests
{
    private static readonly string ResponsePath = Path.Combine(AppContext.BaseDirectory, "response.json");

    [Fact]
    public async Task A_taken_port_prints_one_line_and_exits_with_3()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            using var output = new StringWriter();
            using var error = new StringWriter();

            var code = await MockProgram.RunAsync(["--port", port.ToString(CultureInfo.InvariantCulture)], output, error, TextReader.Null, ResponsePath, CancellationToken.None);

            Assert.Equal(MockProgram.PortInUseExitCode, code);
            Assert.Equal(3, code);
            Assert.Equal($"Port {port} is already in use.{Environment.NewLine}", error.ToString());
            Assert.Empty(output.ToString());
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task A_wrong_command_line_exits_with_2()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await MockProgram.RunAsync(["--fast"], output, error, TextReader.Null, ResponsePath, CancellationToken.None);

        Assert.Equal(2, code);
        Assert.Contains("Unknown argument: --fast", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_mock_prints_ready_and_stops_when_its_input_ends()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var code = await MockProgram.RunAsync(["--port", "0"], output, error, new StringReader("ignored\n"), ResponsePath, CancellationToken.None);

        Assert.Equal(0, code);
        Assert.Equal("ready" + Environment.NewLine, output.ToString());
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task The_mock_stops_when_cancelled()
    {
        using var stop = new CancellationTokenSource();
        using var input = new BlockingReader();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var run = MockProgram.RunAsync(["--port", "0"], output, error, input, ResponsePath, stop.Token);
        await stop.CancelAsync();

        Assert.Equal(0, await run.WaitAsync(TimeSpan.FromSeconds(30)));
    }

    // Input that never ends, like a runner's stdin held open.
    private sealed class BlockingReader : TextReader
    {
        public override ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            => new(new TaskCompletionSource<string?>().Task);
    }
}
