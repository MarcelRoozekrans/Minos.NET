using System.Text;

namespace Minos.Tool.Tests;

/// <summary>The files and the command line every command test needs.</summary>
internal sealed class Harness : IDisposable
{
    public string Dir { get; } = Directory.CreateTempSubdirectory("minos-tool-").FullName;

    public string Out => Path.Combine(Dir, "out");

    public void Dispose() => Directory.Delete(Dir, recursive: true);

    public static string Item(string id)
        => "{\"id\":\"" + id + "\",\"state\":\"s " + id + "\",\"version\":1,\"questions\":{\"q\":{\"type\":\"noul\",\"instructions\":\"?\"}},\"expected\":{\"q\":true}}";

    public string Write(string name, string content)
    {
        var path = Path.Combine(Dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    public string Config(string providerBody = "\"Provider\":\"Custom\",\"BaseAddress\":\"http://fake.local/\",\"Model\":\"fake-1\"", string active = "Fake")
        => Write("minos.json", "{\"Minos\":{\"Active\":\"" + active + "\",\"Providers\":{\"Fake\":{" + providerBody + "}}}}");

    public string Dataset(int items = 3, string name = "data.jsonl", string prefix = "i")
        => Write(name, string.Join('\n', Enumerable.Range(1, items).Select(i => Item(prefix + i))) + "\n");

    public sealed record Run(int Exit, string Stdout, string Stderr);

    public static async Task<Run> Invoke(FakeSystemOne? fake, CancellationToken ct, params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = await MinosCli.RunAsync(args, stdout, stderr, fake is null ? null : () => fake, ct);
        return new Run(exit, stdout.ToString(), stderr.ToString());
    }

    /// <summary>Runs a command that is cancelled when it first writes to stderr, which a command does after it has read its input and before it writes its output.</summary>
    public static async Task<Run> InvokeCancellingOnFirstProblem(params string[] args)
    {
        using var cts = new CancellationTokenSource();
        var stdout = new StringWriter();
        var stderr = new CancelOnWrite(cts);
        var exit = await MinosCli.RunAsync(args, stdout, stderr, null, cts.Token);
        return new Run(exit, stdout.ToString(), stderr.ToString());
    }

    // Cancels on the first character written. Every TextWriter write ends in Write(char), so this sees each overload the tool
    // may call, including the one with a token that .NET 11 adds, which StringWriter does not route through its virtual methods.
    private sealed class CancelOnWrite(CancellationTokenSource cts) : TextWriter
    {
        private readonly StringBuilder _text = new();

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            cts.Cancel();
            _text.Append(value);
        }

        public override string ToString() => _text.ToString();
    }

    public static Task<Run> Invoke(FakeSystemOne? fake, params string[] args) => Invoke(fake, CancellationToken.None, args);
}
