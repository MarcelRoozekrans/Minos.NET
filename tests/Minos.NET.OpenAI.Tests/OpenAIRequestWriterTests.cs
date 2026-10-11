using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Minos.OpenAI.Protocols;
using Minos.Transport;

namespace Minos.OpenAI.Tests;

public sealed class OpenAIRequestWriterTests
{
    private const string Model = "gpt-6-luna";

    private const string AllKindsQuestions =
        """[{"type":"predicate","name":"refund","instructions":"Is this a refund request?"},"""
        + """{"type":"choice","name":"topic","instructions":"Which topic is this about?","choices":[{"value":"billing","description":"Payments and invoices"},{"value":"other"}]},"""
        + """{"type":"score","name":"urgency","instructions":"How urgent is this?","levels":[{"label":"0","description":"Not urgent"},{"label":"1","description":"Soon"},{"label":"2","description":"Right now"}]}]""";

    [Fact]
    public void TextState_AllKinds_WritesTheQuestionsInDefinitionOrder()
    {
        var body = Write(TestSets.AllKinds(), DecisionContent.FromString("I was charged twice."), []);

        Assert.Equal("""{"model":"gpt-6-luna","input":"I was charged twice.","questions":""" + AllKindsQuestions + "}", body);
    }

    [Fact]
    public void JsonObjectState_WithoutImages_IsItsRawTextAsAString()
    {
        using var document = JsonDocument.Parse("""{"a":1}""");
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("q", "x"));

        var body = Write(definition, DecisionContent.FromJson(document.RootElement), []);

        Assert.Equal(
            """{"model":"gpt-6-luna","input":"{\"a\":1}","questions":[{"type":"predicate","name":"q","instructions":"x"}]}""",
            body);
    }

    [Fact]
    public void TextState_WithTwoImages_IsOneUserMessageWithATextPartThenTheImagesInOrder()
    {
        var definition = new QuestionSetDefinition(QuestionDefinition.Noul("q", "x"));
        DecisionImage[] images =
        [
            DecisionImage.FromBytes(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "image/png"),
            DecisionImage.FromBytes(new byte[] { 0xFF, 0xD8 }, "image/jpeg"),
        ];

        var body = Write(definition, DecisionContent.FromString("A photo."), images);

        Assert.Equal(
            """{"model":"gpt-6-luna","input":[{"role":"user","content":[{"type":"input_text","text":"A photo."},"""
            + """{"type":"input_image","image_url":"data:image/png;base64,iVBORw=="},"""
            + """{"type":"input_image","image_url":"data:image/jpeg;base64,/9g="}]}]"""
            + ""","questions":[{"type":"predicate","name":"q","instructions":"x"}]}""",
            body);
    }

    [Fact]
    public void JsonInstructionsAndJsonCriteria_AreSentAsTheirRawJsonText()
    {
        using var instructions = JsonDocument.Parse("""{"ask":"refund?"}""");
        using var option = JsonDocument.Parse("""{"d":1}""");
        using var level = JsonDocument.Parse("""["low"]""");
        var definition = new QuestionSetDefinition(
            QuestionDefinition.Noul("refund", DecisionContent.FromJson(instructions.RootElement)),
            QuestionDefinition.Choice("topic", "t", new OptionDefinition("billing", Criterion.Json(DecisionContent.FromJson(option.RootElement)))),
            QuestionDefinition.Score("urgency", "u", Criterion.Json(DecisionContent.FromJson(level.RootElement)), "high"));

        var body = Write(definition, DecisionContent.FromString("s"), []);

        Assert.Equal(
            """{"model":"gpt-6-luna","input":"s","questions":["""
            + """{"type":"predicate","name":"refund","instructions":"{\"ask\":\"refund?\"}"},"""
            + """{"type":"choice","name":"topic","instructions":"t","choices":[{"value":"billing","description":"{\"d\":1}"}]},"""
            + """{"type":"score","name":"urgency","instructions":"u","levels":[{"label":"0","description":"[\"low\"]"},{"label":"1","description":"high"}]}]}""",
            body);
    }

    [Fact]
    public void QuestionsJson_IsWrittenOnceAndCached()
    {
        var definition = TestSets.AllKinds();

        var first = OpenAIDecisionsProtocol.QuestionsJson(definition);
        var second = OpenAIDecisionsProtocol.QuestionsJson(definition);

        Assert.True(Unsafe.AreSame(ref MemoryMarshal.GetReference(first), ref MemoryMarshal.GetReference(second)));
        Assert.Equal(AllKindsQuestions, Encoding.UTF8.GetString(first));
    }

    [Fact]
    public void Write_ReturnsTheBodyToThePoolOnDispose_AndOnAThrowingStateWriter()
    {
        var pool = new CountingPool();
        var definition = TestSets.AllKinds();

        OpenAIDecisionsProtocol.Instance.WriteRequest(definition, "s", [], Model, pool).Dispose();
        Assert.Throws<InvalidOperationException>(() => OpenAIDecisionsProtocol.Instance.WriteRequest<int>(
            definition, 0, 0, static (_, _, _, _) => throw new InvalidOperationException(), [], Model, pool));

        Assert.Equal(0, pool.Outstanding);
    }

    private static string Write(QuestionSetDefinition definition, DecisionContent state, IReadOnlyList<DecisionImage> images)
    {
        using var body = OpenAIDecisionsProtocol.Instance.WriteRequest(definition, state, images, Model, new CountingPool());
        return Encoding.UTF8.GetString(body.Span);
    }
}
