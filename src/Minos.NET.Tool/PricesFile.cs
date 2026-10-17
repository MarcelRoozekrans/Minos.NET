using System.Globalization;
using System.Text.Json;
using Minos.Evaluation;
using ZeroAlloc.Results;

namespace Minos.Tool;

/// <summary>Reads a prices file: <c>{"prices":[{"model":"…","inputPerMillion":0.042,"outputPerMillion":0,"date":"2026-10-17","source":"…"}]}</c>.</summary>
internal static class PricesFile
{
    /// <summary>Loads the price table.</summary>
    /// <param name="path">The prices file.</param>
    /// <returns>The table, or the message to print.</returns>
    public static Result<PriceTable, string> Load(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("prices", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                return Fail(path, "it needs a \"prices\" array.");
            }

            var prices = new List<ModelPrice>();
            var index = 0;
            foreach (var entry in array.EnumerateArray())
            {
                var price = Read(entry, out var problem);
                if (price is null)
                {
                    return Fail(path, "price " + index.ToString(CultureInfo.InvariantCulture) + " " + problem);
                }

                prices.Add(price);
                index++;
            }

            return Result<PriceTable, string>.Success(new PriceTable(prices));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return Fail(path, ex.Message);
        }
    }

    private static ModelPrice? Read(JsonElement entry, out string problem)
    {
        problem = "is not an object.";
        if (entry.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!Text(entry, "model", out var model) || !Text(entry, "source", out var source))
        {
            problem = "needs a \"model\" and a \"source\" string.";
            return null;
        }

        if (!Number(entry, "inputPerMillion", out var input) || !Number(entry, "outputPerMillion", out var output))
        {
            problem = "needs numeric \"inputPerMillion\" and \"outputPerMillion\".";
            return null;
        }

        if (!Text(entry, "date", out var text) || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            problem = "needs a \"date\" as yyyy-MM-dd.";
            return null;
        }

        problem = string.Empty;
        return new ModelPrice { Model = model, InputPerMillion = input, OutputPerMillion = output, Date = date, Source = source };
    }

    private static bool Text(JsonElement entry, string name, out string value)
    {
        value = string.Empty;
        if (entry.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } s)
        {
            value = s;
            return true;
        }

        return false;
    }

    private static bool Number(JsonElement entry, string name, out double value)
    {
        value = 0;
        return entry.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out value);
    }

    private static Result<PriceTable, string> Fail(string path, string message)
        => Result<PriceTable, string>.Failure("The prices file '" + path + "' is invalid: " + message);
}
