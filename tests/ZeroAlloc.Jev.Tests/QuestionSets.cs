namespace ZeroAlloc.Jev.Tests;

[JevQuestions]
public partial record UrgencyCheck
{
    [Noul("Does this convey urgency?", True = "Explicitly time-sensitive", False = "No urgency expressed")]
    public partial Noul IsUrgent { get; }
}

[JevQuestions]
public partial record MinimalUrgencyCheck
{
    [Noul("Does this convey urgency?")]
    public partial Noul IsUrgent { get; }
}

public enum Department
{
    [Criteria("Payments, invoicing, refunds")]
    Billing,

    [Criteria("Bugs, outages, integrations")]
    Technical,

    [Criteria("Pricing, upgrades, new accounts")]
    Sales,

    Other,
}

[JevQuestions]
public partial record DepartmentRouting
{
    [Choice("Which team should handle this?")]
    public partial Choice<Department> Department { get; }
}

public enum StructuredDepartment
{
    [Criteria("Payments, invoicing, refunds", Examples = ["I was charged twice"], NotFor = ["How much is Pro?"])]
    Billing,

    [Criteria("Bugs, outages, integrations", Examples = new[] { "The API returns 500" })]
    Technical,

    [Criteria("Pricing, upgrades, new accounts", Examples = [], NotFor = [])]
    Sales,

    Other,
}

public enum StructuredSeverity
{
    [Level("Cosmetic", NotFor = ["Data loss"])]
    Low,

    [Level("Blocks work")]
    High,
}

[JevQuestions]
public partial record StructuredRouting
{
    [Choice("Which team should handle this?")]
    public partial Choice<StructuredDepartment> Department { get; }

    [Score("How severe is this?")]
    public partial Score<StructuredSeverity> Severity { get; }
}

public enum Frustration
{
    [Level("Calm")]
    Calm,

    [Level("Frustrated")]
    Frustrated,

    [Level("Very angry")]
    VeryAngry,
}

[JevQuestions]
public partial record FrustrationCheck
{
    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Frustration { get; }
}

[JevQuestions]
public partial class TicketTriage
{
    [Noul("Does `message` ask for a credential?")]
    public partial Noul RequestsCredentials { get; }

    [Choice("Which team should handle `message`?")]
    public partial Choice<Department> Team { get; }

    [Score("How frustrated is the customer?")]
    public partial Score<Frustration> Mood { get; }
}

public enum Priority
{
    [Criteria("Can wait")]
    Low = 10,

    [Criteria("Needs attention", Key = "urgent")]
    High = 20,

    // CA1069 false positive: this alias is deliberate test data for the generator's alias handling
    // (global-constraints.md: "enum members that repeat an earlier member's value (aliases) are not
    // separate options"), not an accidental duplicate.
#pragma warning disable CA1069
    Legacy = 10,
#pragma warning restore CA1069
}

[JevQuestions]
public partial record DuplicateCheck
{
    [Noul(
        """
        {
          "potential_duplicate": {
            "name": "John Smith",
            "location": "Oakland, California",
            "last_employer": "Google"
          },
          "question": "Is the resume for the same person as `potential_duplicate`?"
        }
        """,
        Json = true)]
    public partial Noul IsDuplicate { get; }
}

[JevQuestions]
public partial record EdgeCases
{
    public const string TrickyInstructions = "Quote \" backslash \\ newline \n control \u0001 accent é emoji 😀 backtick `message`";

    [Noul(TrickyInstructions, Key = "tricky")]
    public partial Noul Escaping { get; }

    [Choice("Priority?")]
    public partial Choice<Priority> Priority { get; }
}

// JSON text at the minifier's depth limit of 60. Embedded in a request, a criterion description reaches the 64 levels
// System.Text.Json reads by default, and the instructions one level less.
public static class DeepJson
{
    public const string Text = "[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[\"Is it deep?\"]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]]";
}

public enum DeepOption
{
    [Criteria(DeepJson.Text, Json = true)]
    Shallow,

    [Criteria(DeepJson.Text, Json = true)]
    Deep,
}

[JevQuestions]
public partial record DeepJsonCheck
{
    [Noul(DeepJson.Text, Json = true)]
    public partial Noul IsDeep { get; }

    [Choice(DeepJson.Text, Json = true)]
    public partial Choice<DeepOption> Depth { get; }
}
