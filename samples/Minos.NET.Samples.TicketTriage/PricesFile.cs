using System.Globalization;
using System.Text.Json;
using Minos.Evaluation;

namespace Minos.Samples.TicketTriage;

/// <summary>Reads a prices file: <c>{"prices":[{"model","inputPerMillion","outputPerMillion","date","source"}]}</c>.</summary>
internal static class PricesFile
{
    /// <summary>Loads the price table.</summary>
    /// <param name="path">The prices file.</param>
    /// <returns>The table.</returns>
    /// <exception cref="InvalidDataException">The file is missing, not JSON or not a valid prices file; the message names the path.</exception>
    public static PriceTable Load(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("prices", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                throw Invalid(path, "it needs a \"prices\" array.");
            }

            var prices = new List<ModelPrice>();
            foreach (var entry in array.EnumerateArray())
            {
                prices.Add(Read(path, prices.Count, entry));
            }

            return new PriceTable(prices);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw Invalid(path, ex.Message);
        }
    }

    private static ModelPrice Read(string path, int index, JsonElement entry)
    {
        var where = "price " + index.ToString(CultureInfo.InvariantCulture) + " ";
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(path, where + "is not an object.");
        }

        if (!Text(entry, "model", out var model) || !Text(entry, "source", out var source))
        {
            throw Invalid(path, where + "needs a non-empty \"model\" and \"source\" string.");
        }

        if (!Number(entry, "inputPerMillion", out var input) || !Number(entry, "outputPerMillion", out var output))
        {
            throw Invalid(path, where + "needs numeric \"inputPerMillion\" and \"outputPerMillion\".");
        }

        if (!Text(entry, "date", out var text) || !DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            throw Invalid(path, where + "needs a \"date\" as yyyy-MM-dd.");
        }

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

    private static InvalidDataException Invalid(string path, string message)
        => new("The prices file '" + path + "' is invalid: " + message.ReplaceLineEndings(" "));
}
