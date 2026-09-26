namespace Jev.Net.Generators.Tests;

internal static class Sources
{
    public const string NoulOnly = """
        using Jev.Net;

        namespace Demo;

        [JevQuestions]
        public partial record UrgencyCheck
        {
            [Noul("Does this convey urgency?", True = "Explicitly time-sensitive", False = "No urgency expressed")]
            public partial Noul IsUrgent { get; }
        }
        """;

    public const string ChoiceOnly = """
        using Jev.Net;

        namespace Demo;

        public enum Department
        {
            [Criteria("Payments, invoicing, refunds")] Billing,
            [Criteria("Bugs, outages, integrations", Key = "tech")] Technical,
            Other,
        }

        [JevQuestions]
        public partial record DepartmentRouting
        {
            [Choice("Which team should handle this?")]
            public partial Choice<Department> Department { get; }
        }
        """;

    public const string ScoreOnly = """
        using Jev.Net;

        namespace Demo;

        public enum Frustration
        {
            [Level("Calm")] Calm,
            [Level("Frustrated")] Frustrated,
            [Level("Very angry")] VeryAngry,
        }

        [JevQuestions]
        public partial record FrustrationCheck
        {
            [Score("How frustrated is the customer?")]
            public partial Score<Frustration> Frustration { get; }
        }
        """;

    // A class, not a record, in the global namespace; explicit enum values, an alias member and escaping.
    public const string Mixed = """
        using Jev.Net;

        public enum Team
        {
            [Criteria("Charges, invoices, refunds")] Billing = 10,
            [Criteria("Login, profile, permissions, or security")] Account = 20,
            [Criteria("No listed team fits")] Other = 30,
            Legacy = 10,
        }

        public enum Urgency
        {
            [Level("Can wait")] Low,
            [Level("This week")] Medium,
            [Level("Today")] High,
        }

        [JevQuestions]
        internal partial class TicketTriage
        {
            [Noul("Does `message` ask for a \"credential\"?", False = "No credential is requested")]
            public partial Noul RequestsCredentials { get; }

            [Choice("Which team should handle `message`?", Key = "route_to")]
            public partial Choice<Team> Team { get; }

            [Score("How urgent is `message`?")]
            internal partial Score<Urgency> Urgency { get; }
        }
        """;

    // A Choice over an enum with no members: CS0103 for the shared `buffer` variable if it is only
    // declared when the total option count is positive.
    public const string ChoiceOverEmptyEnum = """
        using Jev.Net;

        namespace Demo;

        public enum EmptyChoice
        {
        }

        [JevQuestions]
        public partial record EmptyChoiceQuestion
        {
            [Choice("Pick one")]
            public partial Choice<EmptyChoice> Answer { get; }
        }
        """;

    // A Score over an enum with no members: same CS0103 risk as ChoiceOverEmptyEnum.
    public const string ScoreOverEmptyEnum = """
        using Jev.Net;

        namespace Demo;

        public enum EmptyScore
        {
        }

        [JevQuestions]
        public partial record EmptyScoreQuestion
        {
            [Score("Rate it")]
            public partial Score<EmptyScore> Answer { get; }
        }
        """;

    // A keyword namespace and a keyword type name: the hint name and the generated declarations
    // must both handle '@'-escaping correctly.
    public const string KeywordNamespaceAndType = """
        using Jev.Net;

        namespace @class;

        [JevQuestions]
        public partial record @event
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;
}
