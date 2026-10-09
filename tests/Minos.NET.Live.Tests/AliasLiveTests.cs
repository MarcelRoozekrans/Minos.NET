using Xunit.Abstractions;

namespace Minos.Live.Tests;

/// <summary>
/// Checks the model aliases live. TypeSafe accepts <c>jev-latest</c> and <c>jev-preview</c>, and the versioned id an
/// alias resolves to: it documents that versioned ids are accepted although its model listing shows only the aliases.
/// OpenRouter accepts <c>jev-latest</c> but not <c>jev-preview</c>, which the guide documents; the test that pins the
/// rejection fails as soon as OpenRouter starts offering the preview, so the guide can be updated. No test assumes the
/// two aliases answer with the same model: they diverge as soon as a preview ships.
/// </summary>
public sealed class AliasLiveTests
{
    private const string Latest = "jev-latest";

    private readonly ITestOutputHelper _output;

    public AliasLiveTests(ITestOutputHelper output) => _output = output;

    [LiveFact(JevProvider.TypeSafe)]
    public async Task TypeSafe_Latest_AnswersWithAVersionedModel()
        => AssertVersioned((await EvaluateAsync(JevProvider.TypeSafe, Latest)).Model);

    [LiveFact(JevProvider.TypeSafe)]
    public async Task TypeSafe_Preview_AnswersWithAVersionedModel()
        => AssertVersioned((await EvaluateAsync(JevProvider.TypeSafe, Live.Preview)).Model);

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

    // OpenRouter prefixes the alias itself and has no typesafe/jev-preview: it answers HTTP 400, "Model
    // typesafe/jev-preview does not exist". Seen first in the Live smoke run 37918889031 on 2026-10-09.
    [LiveFact(JevProvider.OpenRouter)]
    public async Task OpenRouter_Preview_IsRejectedAsAnUnknownModel()
    {
        using var client = Live.Client(JevProvider.OpenRouter);

        var result = await client.EvaluateAsync(Live.Request(Live.Preview));

        if (result.IsSuccess)
        {
            Live.LogServedModel(_output, Live.Preview, result.Value);
        }

        Assert.True(result.IsFailure, "OpenRouter now offers jev-preview: update the guide's Listing models section and this test.");
        Live.LogError(_output, result.Error);
        Assert.Equal(JevErrorKind.Validation, result.Error.Kind);
        Assert.Equal(400, result.Error.StatusCode);
    }

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
