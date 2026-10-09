using Xunit.Abstractions;

namespace ZeroAlloc.Jev.Live.Tests;

/// <summary>
/// Checks that both providers accept the model aliases <c>jev-latest</c> and <c>jev-preview</c>, and that TypeSafe
/// accepts the versioned id an alias resolves to. TypeSafe documents that versioned ids are accepted although its model
/// listing shows only the aliases. No test assumes the two aliases answer with the same model: they diverge as soon as
/// a preview ships.
/// </summary>
public sealed class AliasLiveTests
{
    private const string Latest = "jev-latest";

    private const string Preview = "jev-preview";

    private readonly ITestOutputHelper _output;

    public AliasLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(JevProvider.TypeSafe)]
    public async Task TypeSafe_Latest_AnswersWithAVersionedModel()
        => AssertVersioned((await EvaluateAsync(JevProvider.TypeSafe, Latest)).Model);

    [LiveFact(JevProvider.TypeSafe)]
    public async Task TypeSafe_Preview_AnswersWithAVersionedModel()
        => AssertVersioned((await EvaluateAsync(JevProvider.TypeSafe, Preview)).Model);

    [LiveFact(JevProvider.TypeSafe)]
    public async Task TypeSafe_TheVersionedIdAnAliasResolvesTo_IsAcceptedAsAModel()
    {
        var versioned = (await EvaluateAsync(JevProvider.TypeSafe, Latest)).Model;
        AssertVersioned(versioned);

        var pinned = await EvaluateAsync(JevProvider.TypeSafe, versioned);

        Assert.Equal(versioned, pinned.Model);
    }

    // OpenRouter reports its own model ids, such as typesafe/jev-1.13-20260917, so only their presence is asserted.
    [LiveFact(JevProvider.OpenRouter)]
    public async Task OpenRouter_Latest_Answers()
        => Assert.False(string.IsNullOrWhiteSpace((await EvaluateAsync(JevProvider.OpenRouter, Latest)).Model));

    [LiveFact(JevProvider.OpenRouter)]
    public async Task OpenRouter_Preview_Answers()
        => Assert.False(string.IsNullOrWhiteSpace((await EvaluateAsync(JevProvider.OpenRouter, Preview)).Model));

    private static void AssertVersioned(string model)
        => Assert.True(
            model.Length > 4 && model.StartsWith("jev-", StringComparison.Ordinal) && char.IsAsciiDigit(model[4]),
            "Expected a versioned id such as jev-1.13.0, but the response named '" + model + "'.");

    private async Task<SystemOneResponse> EvaluateAsync(JevProvider provider, string model)
    {
        using var client = Live.Client(provider);

        var result = await client.EvaluateAsync(Live.Request(model));

        if (result.IsFailure)
        {
            Live.LogError(_output, result.Error);
        }

        Assert.True(result.IsSuccess);
        Live.LogServedModel(_output, model, result.Value);
        Live.AssertAnsweredEveryQuestion(result.Value);
        return result.Value;
    }
}
