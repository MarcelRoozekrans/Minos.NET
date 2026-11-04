namespace Minos.Docs.Tests;

#region RunningLocally_Questions
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Minos;
using Minos.Recording;

public enum PaymentTopic
{
    [Criterion("Asks for money back")]
    Refund,

    [Criterion("A charge the customer says they did not make")]
    Fraud,

    [Criterion("Charged more than once for the same purchase")]
    Duplicate,
}

[Questions]
public partial record PaymentReview
{
    [Noul("Does the customer dispute a charge?")]
    public partial Noul IsDisputed { get; }

    [Choice("What is the message about?")]
    public partial Choice<PaymentTopic> Topic { get; }
}
#endregion

#region RunningLocally_Register
public static partial class LocalReadiness
{
    // Minos:Active names the provider section that decides. Minos:Shadow, when set, names the section of a candidate
    // that answers the same decisions in the background; unset, the app records only.
    public static IHostApplicationBuilder AddRecordedDecisions(this IHostApplicationBuilder builder, IDecisionRecordSink sink)
    {
        var configuration = builder.Configuration;
        var active = ProviderSection(configuration, "Minos:Active");

        // One recorder over the app's sink. Text is kept with e-mail addresses masked; the providers still get it whole.
        // A decision on structured state is not recorded at all, since Drop keeps no record of it.
        builder.Services.AddSingleton(provider => new DecisionRecorder(
            sink,
            new RecordingOptions
            {
                Redact = state => state.TryGetString(out var text)
                    ? RedactionResult.Keep(Email().Replace(text!, "[email]"))
                    : RedactionResult.Drop,
            },
            provider.GetService<ILoggerFactory>(),
            provider.GetService<TimeProvider>()));
        builder.Services.AddSingleton<IDecisionRecorder>(provider => provider.GetRequiredService<DecisionRecorder>());

        // The stage added to the registration wraps the client and its standard pipeline, so recording is outermost.
        var client = builder.Services.AddSystemOneDecisionClient(active);
        if (string.IsNullOrWhiteSpace(configuration["Minos:Shadow"]))
        {
            client.UseRecording();
            return builder;
        }

        var shadow = ProviderSection(configuration, "Minos:Shadow");
        if (string.Equals(shadow.Key, active.Key, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Minos:Shadow names {shadow.Key}, which is already Minos:Active: shadow another provider.");
        }

        // The candidate is a client of its own, keyed by its section's name, with no recording stage. On a CPU it takes
        // seconds per decision, so one call at a time, and a time-out well above that. The cap bounds the load.
        builder.Services.AddSystemOneDecisionClient(shadow.Key, shadow);
        // One ShadowOptions per candidate, keyed like it, so every pipeline that shadows it shares one daily cap.
        builder.Services.AddKeyedSingleton(shadow.Key, (provider, _) => new ShadowOptions(provider.GetRequiredKeyedService<IDecisionClient>(shadow.Key), dailyCap: 2000)
        {
            MaxConcurrent = 1,
            Timeout = TimeSpan.FromSeconds(30),
        });

        // The candidate exists only once the container builds it, so the container creates the stage: it calls the
        // factory when it builds the active client, and gives the stage its DecisionRecorder.
        client.UseShadowRecording(provider => provider.GetRequiredKeyedService<ShadowOptions>(shadow.Key));
        return builder;
    }

    private static IConfigurationSection ProviderSection(IConfiguration configuration, string setting)
    {
        var name = configuration[setting];
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Set {setting} to a section under Minos:Providers.");
        }

        var section = configuration.GetSection("Minos:Providers").GetSection(name);
        return section.Exists()
            ? section
            : throw new InvalidOperationException($"{setting} names {name}, but Minos:Providers has no section named {name}.");
    }

    [GeneratedRegex(@"[^\s@]+@[^\s@]+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Email();
}
#endregion

#region RunningLocally_Review
public static class PaymentReviews
{
    // The customer id is the erasure key, so a request to forget the customer reaches this decision.
    public static async Task<(PaymentReview? Review, string? DecisionId)> ReviewAsync(
        IDecisionClient client, string customerId, string text, CancellationToken cancellationToken)
    {
        using var scope = DecisionRecording.Begin(erasureKey: customerId);
        var result = await client.EvaluateAsync<PaymentReview>(text, cancellationToken);

        // Keep the id with the payment, so the desk's answer can be attached to this decision later.
        return (result.IsSuccess ? result.Value : null, scope.DecisionId);
    }
}
#endregion

#region RunningLocally_Outcomes
public static class PaymentOutcomes
{
    // The payments desk settled the case: the right answers, which both the primary and the candidate are measured on.
    public static ValueTask ConfirmAsync(
        IDecisionRecorder recorder, string decisionId, bool disputed, PaymentTopic topic, CancellationToken cancellationToken)
        => recorder.AddOutcomeAsync(
            decisionId,
            DecisionOutcome.Confirmed("payments-desk", DateTimeOffset.UtcNow, new Dictionary<string, ConfirmedAnswer>
            {
                ["is_disputed"] = ConfirmedAnswer.ForNoul(disputed),
                ["topic"] = ConfirmedAnswer.ForChoice(TopicKey(topic)),
            }),
            cancellationToken);

    // The option keys the question set sends for each topic.
    private static string TopicKey(PaymentTopic topic) => topic switch
    {
        PaymentTopic.Refund => "refund",
        PaymentTopic.Fraud => "fraud",
        PaymentTopic.Duplicate => "duplicate",
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, null),
    };
}
#endregion

/// <summary>The two <c>appsettings.json</c> texts the running-locally page shows, before and after the switch.</summary>
internal static class RunningLocallyConfig
{
    public const string Before = """
        {
          "Minos": {
            "Active": "TypeSafe",
            "Shadow": "Local",
            "Providers": {
              "TypeSafe": { "Provider": "TypeSafe" },
              "Local": {
                "Provider": "Custom",
                "BaseAddress": "http://localhost:8000/",
                "Model": "Mapika/decider-2b-GGUF:Q4_K_M"
              }
            }
          }
        }
        """;

    public const string After = """
        {
          "Minos": {
            "Active": "Local",
            "Providers": {
              "TypeSafe": { "Provider": "TypeSafe" },
              "Local": {
                "Provider": "Custom",
                "BaseAddress": "http://localhost:8000/",
                "Model": "Mapika/decider-2b-GGUF:Q4_K_M"
              }
            }
          }
        }
        """;
}
