namespace Minos.Docs.Tests;

using Minos.SystemOne;

#region ClientAndErrors_OpenAI
using Minos;
using Minos.OpenAI;

public static class OpenAIClients
{
    // The key is required. Without ApiKey the client reads OPENAI_API_KEY, and without a key it throws when it is
    // created. The model defaults to gpt-6-luna and the address to https://api.openai.com/v1/.
    public static OpenAIDecisionClient Create(string apiKey)
        => new(new OpenAIDecisionClientOptions { ApiKey = apiKey });

    // The same typed call as on any IDecisionClient. The set is checked against what OpenAI can express before
    // anything is sent.
    public static async Task<string> TriageAsync(
        OpenAIDecisionClient client, string message, CancellationToken cancellationToken)
    {
        var result = await client.EvaluateAsync<InboxCheck>(message, cancellationToken);
        return result.IsFailure
            ? ClientFailures.Describe(result.Error)
            : result.Value.IsUrgent.Value ? "urgent" : "can wait";
    }
}
#endregion

public static class OpenAIRefusals
{
    #region ClientAndErrors_Refused
    // A refusal fails the whole call: the answers to the other questions are discarded, because a typed set needs every
    // answer. Failures names each refused question, with rule "refusal". Retrying sends the same request and gets the
    // same refusal, so change the state or the questions instead.
    public static IReadOnlyList<string> RefusedQuestions(DecisionError error)
    {
        var keys = new List<string>();
        if (error.Kind != DecisionErrorKind.Refused)
        {
            return keys;
        }

        foreach (var failure in error.Failures)
        {
            if (failure.QuestionKey is { } key)
            {
                keys.Add(key);
            }
        }

        return keys;
    }
    #endregion
}

#region ClientAndErrors_ImageQuestions
// An ordinary question set. The images go with the state at call time, so the same set works with or without them.
[Questions]
public partial record ReturnPhotoCheck
{
    [Noul("Does the product in the photo look damaged?")]
    public partial Noul IsDamaged { get; }
}
#endregion

public static class ImageCalls
{
    #region ClientAndErrors_Images
    // Each image is a DecisionImage: bytes with their media type, or a data URL. PNG, JPEG, WebP and GIF are accepted,
    // and anything else throws ArgumentException. FromBytes copies the bytes, so the buffer can be reused at once.
    public static async Task<string> CheckReturnAsync(
        IDecisionClient client, ReadOnlyMemory<byte> photo, string screenshotDataUrl, CancellationToken cancellationToken)
    {
        IReadOnlyList<DecisionImage> images =
        [
            DecisionImage.FromBytes(photo, "image/jpeg"),
            DecisionImage.FromDataUrl(screenshotDataUrl),
        ];

        // The overload with images takes them after the state. A provider without image support returns Unsupported
        // with MIN206, and nothing is sent.
        var result = await client.EvaluateAsync<ReturnPhotoCheck>(
            "A return request for a kettle.", images, cancellationToken);
        return result.IsFailure
            ? ClientFailures.Describe(result.Error)
            : result.Value.IsDamaged.Value ? "damaged" : "not damaged";
    }
    #endregion
}

public static class OpenRouterModels
{
    #region ClientAndErrors_OpenRouterImages
    // OpenRouter mixes text and image models, so images are off for the preset. Switch them on for a model that reads
    // images. Perplexity's decider read our test image correctly on 2026-10-11.
    public static SystemOneDecisionClientOptions PerplexityWithImages()
        => new()
        {
            Provider = SystemOneProvider.OpenRouter,
            Model = "perplexity/pplx-decider-v1.1-27b",
            Capabilities = new DecisionCapabilities { SupportsImages = true },
        };
    #endregion

    #region ClientAndErrors_OpenRouterModels
    // OpenRouter serves Clef and Luna in the System One format, so the OpenRouter preset reaches them by model id.
    // The request is System One: the OpenRouter column of the provider matrix applies, not the OpenAI one.
    public static SystemOneDecisionClientOptions Clef()
        => new() { Provider = SystemOneProvider.OpenRouter, Model = "cloudflare/clef-flash" };

    // Luna through OpenRouter needs an account that allows endpoints which retain data; see the notes above.
    public static SystemOneDecisionClientOptions Luna()
        => new() { Provider = SystemOneProvider.OpenRouter, Model = "openai/gpt-6-luna-decisions" };
    #endregion
}
