using System.Text.Json;

namespace Jev.Net.Generators.Tests;

public sealed class SnakeCaseTests
{
    [Theory]
    [InlineData("RequestsCredentials")]
    [InlineData("IsUrgent")]
    [InlineData("Team")]
    [InlineData("A")]
    [InlineData("ABC")]
    [InlineData("XMLReader")]
    [InlineData("SHA512Hash")]
    [InlineData("HTTPStatusCode")]
    [InlineData("IOStream")]
    [InlineData("Value2")]
    [InlineData("Level10Plus")]
    [InlineData("X1Y2")]
    [InlineData("already_snake")]
    [InlineData("camelCase")]
    [InlineData("VeryAngry")]
    [InlineData("Über")]
    public void Convert_MatchesSystemTextJson(string name)
        => Assert.Equal(JsonNamingPolicy.SnakeCaseLower.ConvertName(name), SnakeCase.Convert(name));

    [Theory]
    [InlineData("RequestsCredentials", "requests_credentials")]
    [InlineData("IsUrgent", "is_urgent")]
    [InlineData("XMLReader", "xml_reader")]
    [InlineData("Billing", "billing")]
    public void Convert_ProducesWireKeys(string name, string expected)
        => Assert.Equal(expected, SnakeCase.Convert(name));
}
