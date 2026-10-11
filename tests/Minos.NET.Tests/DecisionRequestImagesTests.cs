using System.Text;
using System.Text.Json;

namespace Minos.Tests;

/// <summary>Covers <see cref="DecisionRequest.Images"/> and the image overloads of <see cref="DecisionClientExtensions"/>.</summary>
public sealed class DecisionRequestImagesTests
{
    private static readonly DecisionImage Png = DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png");
    private static readonly DecisionImage Gif = DecisionImage.FromBytes(new byte[] { 0x47, 0x49, 0x46 }, "image/gif");

    private static DecisionRequest NewRequest() => new(QuestionSets.UrgencyDefinition(), DecisionContent.FromString("s"));

    private static DecisionImage[] Pair() => [Png, Gif];

    [Fact]
    public void Images_DefaultsToEmpty() => Assert.Empty(NewRequest().Images);

    [Fact]
    public void Images_IsCopied()
    {
        var list = new List<DecisionImage> { Png };
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), DecisionContent.FromString("s")) { Images = list };

        list.Add(Gif);

        Assert.Equal(new[] { Png }, request.Images);
    }

    [Fact]
    public void Images_NullThrows()
        => Assert.Throws<ArgumentNullException>("value", () => new DecisionRequest(QuestionSets.UrgencyDefinition(), DecisionContent.FromString("s")) { Images = null! });

    [Fact]
    public void With_CopiesImages()
    {
        var request = new DecisionRequest(QuestionSets.UrgencyDefinition(), DecisionContent.FromString("s")) { Images = Pair() };

        var copy = request with { RetryAttempt = 1 };

        Assert.Equal(request.Images, copy.Images);
        Assert.Equal(1, copy.RetryAttempt);
    }

    [Fact]
    public void Equality_ComparesImagesByValue()
    {
        var definition = QuestionSets.UrgencyDefinition();
        var state = DecisionContent.FromString("s");
        var request = new DecisionRequest(definition, state) { Images = Pair() };

        Assert.Equal(request, new DecisionRequest(definition, state) { Images = Pair() });
        Assert.Equal(request.GetHashCode(), new DecisionRequest(definition, state) { Images = Pair() }.GetHashCode());
        Assert.NotEqual(request, new DecisionRequest(definition, state) { Images = [Png] });
        Assert.NotEqual(request, new DecisionRequest(definition, state) { Images = [Gif, Png] });
        Assert.NotEqual(request, new DecisionRequest(definition, state));
        Assert.NotEqual(request, request with { RetryAttempt = 1 });
        Assert.NotEqual(request, request with { Model = "m" });
        Assert.NotEqual(request, new DecisionRequest(QuestionSets.UrgencyDefinition(), state) { Images = Pair() });
        Assert.False(request.Equals((DecisionRequest?)null));
    }

    [Fact]
    public async Task Typed_text_overloads_carry_the_images_and_the_same_state()
    {
        var fake = new ClientTestKit.CapturingClient();
        using var cancellation = new CancellationTokenSource();

        await fake.EvaluateAsync<UrgencyCheck>("a ticket");
        await fake.EvaluateAsync<UrgencyCheck>("a ticket", Pair());
        await fake.EvaluateAsync<UrgencyCheck>("a ticket", Pair(), cancellation.Token);

        Assert.Equal(3, fake.Requests.Count);
        AssertSame(fake.Requests[0], fake.Requests[1]);
        AssertSame(fake.Requests[0], fake.Requests[2]);
        Assert.Equal(cancellation.Token, fake.LastToken);
        Assert.Empty(fake.Requests[0].Images);
    }

    [Fact]
    public async Task Typed_json_overloads_carry_the_images_and_the_same_state()
    {
        var fake = new ClientTestKit.CapturingClient();
        using var cancellation = new CancellationTokenSource();
        using var document = JsonDocument.Parse("""{"subject":"s"}""");

        await fake.EvaluateAsync<UrgencyCheck>(document.RootElement);
        await fake.EvaluateAsync<UrgencyCheck>(document.RootElement, Pair());
        await fake.EvaluateAsync<UrgencyCheck>(document.RootElement, Pair(), cancellation.Token);

        AssertSame(fake.Requests[0], fake.Requests[1]);
        AssertSame(fake.Requests[0], fake.Requests[2]);
        Assert.Equal(cancellation.Token, fake.LastToken);
    }

    [Fact]
    public async Task Typed_utf8_overloads_carry_the_images_and_the_same_state()
    {
        var fake = new ClientTestKit.CapturingClient();
        using var cancellation = new CancellationTokenSource();
        var utf8 = Encoding.UTF8.GetBytes("""{"subject":"s"}""");

        await fake.EvaluateUtf8Async<UrgencyCheck>(utf8);
        await fake.EvaluateUtf8Async<UrgencyCheck>(utf8, Pair());
        await fake.EvaluateUtf8Async<UrgencyCheck>(utf8, Pair(), cancellation.Token);

        AssertSame(fake.Requests[0], fake.Requests[1]);
        AssertSame(fake.Requests[0], fake.Requests[2]);
        Assert.Equal(cancellation.Token, fake.LastToken);
    }

    [Fact]
    public async Task Typed_state_overloads_carry_the_images_and_the_same_state()
    {
        var fake = new ClientTestKit.CapturingClient();
        using var cancellation = new CancellationTokenSource();
        var state = new TicketContext("s", "b");
        var info = TicketContextJsonContext.Default.TicketContext;

        await fake.EvaluateAsync<TicketUrgency, TicketContext>(state, info);
        await fake.EvaluateAsync<TicketUrgency, TicketContext>(state, info, Pair());
        await fake.EvaluateAsync<TicketUrgency, TicketContext>(state, info, Pair(), cancellation.Token);

        AssertSame(fake.Requests[0], fake.Requests[1]);
        AssertSame(fake.Requests[0], fake.Requests[2]);
        Assert.Equal(cancellation.Token, fake.LastToken);
    }

    [Fact]
    public async Task Built_set_overloads_carry_the_images_and_the_same_state()
    {
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value;
        var fake = new ClientTestKit.CapturingClient(set.Definition);
        using var cancellation = new CancellationTokenSource();
        var state = DecisionContent.FromString("a ticket");

        await fake.EvaluateAsync(set, state);
        await fake.EvaluateAsync(set, state, Pair());
        await fake.EvaluateAsync(set, state, Pair(), cancellation.Token);

        AssertSame(fake.Requests[0], fake.Requests[1]);
        AssertSame(fake.Requests[0], fake.Requests[2]);
        Assert.Equal(cancellation.Token, fake.LastToken);
        await Assert.ThrowsAsync<ArgumentException>(async () => await fake.EvaluateAsync(set, default, Pair()));
    }

    [Fact]
    public async Task Overloads_with_no_images_send_an_empty_list()
    {
        var fake = new ClientTestKit.CapturingClient();

        await fake.EvaluateAsync<UrgencyCheck>("a ticket", []);

        Assert.Empty(fake.OnlyRequest().Images);
    }

    [Fact]
    public void Overloads_RejectNullImages()
    {
        var fake = new ClientTestKit.CapturingClient();
        var set = QuestionSet.CreateBuilder().Noul("is_urgent", "Is this urgent?", out _).Build().Value;
        using var document = JsonDocument.Parse("""{"subject":"s"}""");
        var utf8 = Encoding.UTF8.GetBytes("""{"subject":"s"}""");
        var ticket = new TicketContext("s", "b");
        var info = TicketContextJsonContext.Default.TicketContext;
        var state = DecisionContent.FromString("s");
        var token = CancellationToken.None;

        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>("s", null!).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>("s", null!, token).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>(document.RootElement, null!).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<UrgencyCheck>(document.RootElement, null!, token).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateUtf8Async<UrgencyCheck>(utf8, null!).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateUtf8Async<UrgencyCheck>(utf8, null!, token).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<TicketUrgency, TicketContext>(ticket, info, null!).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync<TicketUrgency, TicketContext>(ticket, info, null!, token).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync(set, state, null!).AsTask()).ParamName);
        Assert.Equal("images", ClientTestKit.ThrowsSynchronously<ArgumentNullException>(() => fake.EvaluateAsync(set, state, null!, token).AsTask()).ParamName);
        Assert.Empty(fake.Requests);
    }

    private static void AssertSame(DecisionRequest plain, DecisionRequest withImages)
    {
        Assert.Equal(plain.State, withImages.State);
        Assert.Same(plain.Definition, withImages.Definition);
        Assert.Null(withImages.Model);
        Assert.Equal(new[] { Png, Gif }, withImages.Images);
    }
}
