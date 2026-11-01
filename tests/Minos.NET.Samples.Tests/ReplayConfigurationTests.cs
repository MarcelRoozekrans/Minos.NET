using Minos.Samples.ShadowReplay;

namespace Minos.Samples.Tests;

/// <summary>The replay reads its two clients from the Minos:Providers sections of a config file, with the environment on top.</summary>
public sealed class ReplayConfigurationTests : IDisposable
{
    private const string KeyVariable = "TYPESAFE_API_KEY";
    private const string ModelVariable = "Minos__Providers__Local__Model";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"replay-config-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Resolve_ReadsTheNamedSection()
    {
        File.WriteAllText(_path, """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:8000/","Model":"some-model"}}}}""");

        var options = ReplayConfiguration.Resolve(_path, "local");

        Assert.Equal(new Uri("http://127.0.0.1:8000/"), options.BaseAddress);
        Assert.Equal("some-model", options.Model);
    }

    [Fact]
    public void Resolve_LetsTheEnvironmentOverrideTheFile()
    {
        File.WriteAllText(_path, """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:8000/","Model":"from-file"}}}}""");
        var before = Environment.GetEnvironmentVariable(ModelVariable);
        try
        {
            Environment.SetEnvironmentVariable(ModelVariable, "from-environment");

            var options = ReplayConfiguration.Resolve(_path, "Local");

            Assert.Equal("from-environment", options.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ModelVariable, before);
        }
    }

    [Fact]
    public void Resolve_NamesTheConfiguredSectionsWhenOneIsMissing()
    {
        File.WriteAllText(_path, """{"Minos":{"Providers":{"Local":{"Provider":"Custom","BaseAddress":"http://127.0.0.1:8000/","Model":"m"}}}}""");

        var ex = Assert.Throws<InvalidOperationException>(() => ReplayConfiguration.Resolve(_path, "Nope"));

        Assert.Contains("Nope", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Local", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_ReadsTheRepositoryConfig()
    {
        var config = Path.Combine(Repository.Root, "docs", "reports", "minos.json");

        var before = Environment.GetEnvironmentVariable(KeyVariable);
        try
        {
            // The TypeSafe section holds no key; validating it needs one, and nothing is sent.
            Environment.SetEnvironmentVariable(KeyVariable, "test-key");

            var typeSafe = ReplayConfiguration.Resolve(config, "TypeSafe");
            var local = ReplayConfiguration.Resolve(config, "Local");

            Assert.Equal("jev-latest", typeSafe.Model);
            Assert.Equal("Mapika/decider-2b-GGUF:Q4_K_M", local.Model);
        }
        finally
        {
            Environment.SetEnvironmentVariable(KeyVariable, before);
        }
    }
}
