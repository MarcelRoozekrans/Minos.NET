using Minos.SystemOne;
using Minos.SystemOne.Transport;

namespace Minos.Tests;

public sealed class PresetTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] values)
    {
        var map = values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);
        return key => map.TryGetValue(key, out var value) ? value : null;
    }

    [Theory]
    [InlineData(SystemOneProvider.TypeSafe, "https://api.typesafe.ai/", "typesafe")]
    [InlineData(SystemOneProvider.OpenRouter, "https://openrouter.ai/api/", "openrouter")]
    public void Hosted_presets_keep_their_addresses_and_default_model(SystemOneProvider provider, string address, string name)
    {
        var settings = SystemOneClientSettings.Resolve(new SystemOneClientOptions { Provider = provider, ApiKey = "k" }, Env());

        Assert.Equal(new Uri(address), settings.BaseAddress);
        Assert.Equal("v1/systemone", settings.PathFor("jev-latest"));
        Assert.Equal("jev-latest", settings.Model);
        Assert.Equal(name, settings.ProviderName);
        Assert.Equal("Bearer k", settings.Authorization);
    }

    [Fact]
    public void Cloudflare_puts_the_account_in_the_address_and_the_model_in_the_path()
    {
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions { Provider = SystemOneProvider.CloudflareWorkersAi, Model = "clef-flash" },
            Env(("CLOUDFLARE_API_TOKEN", "t"), ("CLOUDFLARE_ACCOUNT_ID", "abc123")));

        Assert.Equal(new Uri("https://api.cloudflare.com/client/v4/accounts/abc123/ai/run/@cf/cloudflare/"), settings.BaseAddress);
        Assert.Equal("clef", settings.PathFor("clef"));
        Assert.Equal("cloudflare", settings.ProviderName);
        Assert.Equal("Bearer t", settings.Authorization);
    }

    [Theory]
    [InlineData(SystemOneProvider.Custom, "custom")]
    [InlineData(SystemOneProvider.VllmSemanticRouter, "vllm-sr")]
    public void Local_presets_need_no_key_and_ignore_vendor_key_variables(SystemOneProvider provider, string name)
    {
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions { Provider = provider, BaseAddress = new Uri("http://localhost:8000"), Model = "m" },
            Env(("TYPESAFE_API_KEY", "leak"), ("OPENROUTER_API_KEY", "leak"), ("CLOUDFLARE_API_TOKEN", "leak")));

        Assert.Null(settings.ApiKey);
        Assert.Null(settings.Authorization);
        Assert.Equal(new Uri("http://localhost:8000/"), settings.BaseAddress);
        Assert.Equal("v1/systemone", settings.PathFor("m"));
        Assert.Equal(name, settings.ProviderName);
    }

    [Fact]
    public void Custom_path_overrides_the_default()
    {
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions { Provider = SystemOneProvider.Custom, BaseAddress = new Uri("http://localhost:8080/api/"), Path = "decide/v1/systemone", Model = "m", ApiKey = "k" },
            Env());

        Assert.Equal("decide/v1/systemone", settings.PathFor("m"));
        Assert.Equal("Bearer k", settings.Authorization);
    }

    [Theory]
    [InlineData(" decide/v1/systemone\t", "decide/v1/systemone")]
    [InlineData("   ", "v1/systemone")]
    [InlineData("", "v1/systemone")]
    public void Path_is_trimmed_and_a_blank_one_counts_as_unset(string path, string expected)
    {
        var settings = SystemOneClientSettings.Resolve(
            new SystemOneClientOptions { Provider = SystemOneProvider.Custom, BaseAddress = new Uri("http://localhost:8080/api/"), Path = path, Model = "m" },
            Env());

        Assert.Equal(expected, settings.PathFor("m"));
    }

    [Fact]
    public void A_blank_path_on_a_hosted_preset_counts_as_unset()
        => Assert.Equal(
            "v1/systemone",
            SystemOneClientSettings.Resolve(new SystemOneClientOptions { ApiKey = "k", Path = " " }, Env()).PathFor("jev-latest"));

    [Theory]
    [MemberData(nameof(InvalidPresetCases))]
    public void Invalid_preset_options_throw(SystemOneClientOptions options, string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => SystemOneClientSettings.Resolve(options, Env(("CLOUDFLARE_API_TOKEN", "t"))));
        Assert.StartsWith(message, exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<SystemOneClientOptions, string> InvalidPresetCases() => new()
    {
        { new() { Provider = SystemOneProvider.CloudflareWorkersAi, Model = "clef" }, "CloudflareWorkersAi needs an AccountId." },
        { new() { Provider = SystemOneProvider.CloudflareWorkersAi, Model = "clef", AccountId = "a/b" }, "The Cloudflare account id must contain only letters and digits." },
        { new() { Provider = SystemOneProvider.CloudflareWorkersAi, AccountId = "a" }, "CloudflareWorkersAi has no default model." },
        { new() { Provider = SystemOneProvider.CloudflareWorkersAi, AccountId = "a", Model = "@cf/x" }, "A Cloudflare model must be a single name" },
        { new() { Provider = SystemOneProvider.Custom, Model = "m" }, "Custom needs a BaseAddress." },
        { new() { Provider = SystemOneProvider.VllmSemanticRouter, Model = "m" }, "VllmSemanticRouter needs a BaseAddress." },
        { new() { Provider = SystemOneProvider.Custom, BaseAddress = new("http://h/") }, "Custom has no default model." },
        { new() { Provider = SystemOneProvider.TypeSafe, ApiKey = "k", Path = "x" }, "Path applies only to the Custom and VllmSemanticRouter providers." },
        { new() { Provider = SystemOneProvider.Custom, BaseAddress = new("http://h/"), Model = "m", Path = "/x" }, "The path must be relative" },
        { new() { Provider = SystemOneProvider.Custom, BaseAddress = new("http://h/"), Model = "m", Path = "a/../b" }, "The path must be relative" },
        { new() { Provider = SystemOneProvider.TypeSafe, ApiKey = "k", AccountId = "a" }, "AccountId applies only to the CloudflareWorkersAi provider." },
    };
}
