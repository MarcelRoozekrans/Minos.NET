using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Minos.Recording;

namespace Minos.Extensions.AI.Tests;

/// <summary>The <c>AddChatDecisionClient</c> registrations: the default client, the keyed client, the recording stage and a missing chat client.</summary>
public sealed class DependencyInjectionTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private static readonly QuestionDefinition Team = QuestionDefinition.Choice(
        "team", "Which team handles it?", new OptionDefinition("billing", null), new OptionDefinition("shipping", null), new OptionDefinition("sales", null));

    [Fact]
    public void Default_registration_resolves_a_decision_client_over_the_container_chat_client()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        var services = new ServiceCollection();
        services.AddChatClient(chat);
        services.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized);

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();

        Assert.NotNull(client.GetService<ChatDecisionClient>());
    }

    [Fact]
    public async Task Default_registration_asks_the_container_chat_client_with_its_own_options()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        var services = new ServiceCollection();
        services.AddChatClient(chat);
        services.AddChatDecisionClient(options =>
        {
            options.ProbabilityMode = ProbabilityMode.Verbalized;
            options.Model = "configured-model";
        });

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), DecisionContent.FromString("I was charged twice.")));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var options = Assert.Single(chat.Calls).Options;
        Assert.NotNull(options);
        Assert.Equal("configured-model", options.ModelId);
        Assert.Null(options.RawRepresentationFactory);
    }

    [Fact]
    public void Each_registration_gets_its_own_options_instance()
    {
        var instances = new List<ChatDecisionClientOptions>();
        var services = new ServiceCollection();
        services.AddChatClient(Answering("A"));
        services.AddKeyedChatClient("one", Answering("A"));
        services.AddChatDecisionClient(instances.Add);
        services.AddChatDecisionClient("one", instances.Add);

        using var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IDecisionClient>();
        _ = provider.GetRequiredKeyedService<IDecisionClient>("one");

        Assert.Equal(2, instances.Count);
        Assert.NotSame(instances[0], instances[1]);
    }

    [Fact]
    public async Task UseRecording_chains_and_records_through_the_container_recorder()
    {
        var sink = new InMemoryDecisionRecordSink();
        var services = new ServiceCollection();
        services.AddSingleton(_ => new DecisionRecorder(sink));
        services.AddChatClient(Answering("""{"A":0.7,"B":0.2,"C":0.1}"""));
        services.AddChatDecisionClient(options => options.ProbabilityMode = ProbabilityMode.Verbalized).UseRecording();

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IDecisionClient>();
        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), DecisionContent.FromString("I was charged twice.")));
        await provider.GetRequiredService<DecisionRecorder>().FlushAsync().AsTask().WaitAsync(Bound);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(sink.Records);
        Assert.NotNull(client.GetService<ChatDecisionClient>());
    }

    [Fact]
    public void Keyed_registration_resolves_the_keyed_decision_client_over_the_keyed_chat_client()
    {
        var chat = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        var services = new ServiceCollection();
        services.AddKeyedChatClient("triage", chat);
        services.AddChatDecisionClient("triage");

        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<IDecisionClient>("triage");

        Assert.NotNull(client.GetService<ChatDecisionClient>());
        Assert.Null(provider.GetService<IDecisionClient>());
    }

    [Fact]
    public async Task Keyed_registration_asks_the_keyed_chat_client()
    {
        var keyed = Answering("""{"A":0.7,"B":0.2,"C":0.1}""");
        var other = Answering("""{"A":0.1,"B":0.1,"C":0.8}""");
        var services = new ServiceCollection();
        services.AddKeyedChatClient("triage", keyed);
        services.AddChatClient(other);
        services.AddChatDecisionClient("triage", options => options.ProbabilityMode = ProbabilityMode.Verbalized);

        await using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredKeyedService<IDecisionClient>("triage");
        var result = await client.EvaluateAsync(new DecisionRequest(new QuestionSetDefinition(Team), DecisionContent.FromString("I was charged twice.")));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Single(keyed.Calls);
        Assert.Empty(other.Calls);
    }

    [Fact]
    public void Missing_chat_client_throws_naming_IChatClient()
    {
        var services = new ServiceCollection();
        services.AddChatDecisionClient();

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IDecisionClient>());

        Assert.Contains("IChatClient", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_keyed_chat_client_throws_naming_IChatClient()
    {
        var services = new ServiceCollection();
        services.AddChatClient(Answering("A"));
        services.AddChatDecisionClient("triage");

        using var provider = services.BuildServiceProvider();
        var exception = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<IDecisionClient>("triage"));

        Assert.Contains("IChatClient", exception.ToString(), StringComparison.Ordinal);
    }

    private static ScriptedChatClient Answering(string reply) => new((_, _) => ChatCompletions.WithoutLogprobs(reply));
}
