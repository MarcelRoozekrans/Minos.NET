namespace ZeroAlloc.Jev.Generator.Tests;

internal static class Sources
{
    public const string NoulOnly = """
        using ZeroAlloc.Jev;

        namespace Demo;

        [JevQuestions]
        public partial record UrgencyCheck
        {
            [Noul("Does this convey urgency?", True = "Explicitly time-sensitive", False = "No urgency expressed")]
            public partial Noul IsUrgent { get; }
        }
        """;

    public const string ChoiceOnly = """
        using ZeroAlloc.Jev;

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
        using ZeroAlloc.Jev;

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
        using ZeroAlloc.Jev;

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

    // A Choice over an enum with no members: JEV001 makes the set invalid, so the generator emits nothing.
    public const string ChoiceOverEmptyEnum = """
        using ZeroAlloc.Jev;

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

    // A Score over an enum with no members: JEV002 makes the set invalid, so the generator emits nothing.
    public const string ScoreOverEmptyEnum = """
        using ZeroAlloc.Jev;

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
        using ZeroAlloc.Jev;

        namespace @class;

        [JevQuestions]
        public partial record @event
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;

    // A set with a State type: the generated declaration implements the two-argument
    // IJevQuestionSet<TSelf, TState>, naming the state type fully qualified.
    public const string WithState = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public sealed record TicketContext(string CustomerId);

        [JevQuestions(State = typeof(TicketContext))]
        public partial record Set
        {
            [Noul("Is this urgent?")]
            public partial Noul IsUrgent { get; }
        }
        """;

    // A State type that is an abstract class: a legitimate polymorphic state (a derived instance's JsonTypeInfo
    // handles the hierarchy), unlike a static class, which JEV107 rejects because it has no value at all.
    public const string WithAbstractState = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public abstract record StateBase;

        [JevQuestions(State = typeof(StateBase))]
        public partial record AbstractStateSet
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;

    // A State type nested inside another type: the fully qualified name must include the outer type.
    public const string WithNestedState = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public class Outer
        {
            public sealed record Inner(int Value);
        }

        [JevQuestions(State = typeof(Outer.Inner))]
        public partial record NestedStateSet
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;

    // A State type that is a closed generic instantiation, not the open generic type definition JEV107 rejects.
    public const string WithClosedGenericState = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public sealed class Wrapper<T>;

        [JevQuestions(State = typeof(Wrapper<int>))]
        public partial record ClosedGenericStateSet
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;

    // A State type that is an array: a JSON array is a legitimate state shape, and the generated set implements
    // IJevQuestionSet<TSelf, TElement[]>, naming the element type fully qualified with the array suffix.
    public const string WithArrayState = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public sealed record ChatMessage(string Role, string Content);

        [JevQuestions(State = typeof(ChatMessage[]))]
        public partial record ArrayStateSet
        {
            [Noul("q")]
            public partial Noul Answer { get; }
        }
        """;

    // A keyword-named question property and a keyword-named enum member: both must be emitted as
    // '@'-escaped identifiers while the JSON ids they produce stay the bare keyword text.
    public const string KeywordMembers = """
        using ZeroAlloc.Jev;

        namespace Demo;

        public enum Verdict
        {
            [Criteria("Approved")] Approved,
            [Criteria("Denied")] @for,
        }

        [JevQuestions]
        public partial record KeywordMemberCheck
        {
            [Choice("What is the verdict?")]
            public partial Choice<Verdict> @class { get; }
        }
        """;
}
