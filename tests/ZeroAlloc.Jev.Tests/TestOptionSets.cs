using System.Text.Json;

namespace ZeroAlloc.Jev.Tests;

internal enum Color
{
    Red,
    Green = 5,
    Blue,
}

internal enum Urgency
{
    Low,
    Medium,
    High,
}

/// <summary>Hand-written stand-in for a generated Choice option set: wire keys red, green, blue.</summary>
internal sealed class ColorOptions : JevOptionSet<Color>
{
    private ColorOptions()
    {
    }

    public static ColorOptions Instance { get; } = new();

    public override int Count => 3;

    public override Color this[int index] => index switch
    {
        0 => Color.Red,
        1 => Color.Green,
        2 => Color.Blue,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public override int IndexOf(Color value) => value switch
    {
        Color.Red => 0,
        Color.Green => 1,
        Color.Blue => 2,
        _ => -1,
    };

    public override int IndexOfKey(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("red"u8))
        {
            return 0;
        }

        if (reader.ValueTextEquals("green"u8))
        {
            return 1;
        }

        return reader.ValueTextEquals("blue"u8) ? 2 : -1;
    }
}

/// <summary>Hand-written stand-in for a generated Score level set: wire keys "0", "1", "2".</summary>
internal sealed class UrgencyLevels : JevOptionSet<Urgency>
{
    private UrgencyLevels()
    {
    }

    public static UrgencyLevels Instance { get; } = new();

    public override int Count => 3;

    public override Urgency this[int index] => index switch
    {
        0 => Urgency.Low,
        1 => Urgency.Medium,
        2 => Urgency.High,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public override int IndexOf(Urgency value) => value switch
    {
        Urgency.Low => 0,
        Urgency.Medium => 1,
        Urgency.High => 2,
        _ => -1,
    };

    public override int IndexOfKey(ref Utf8JsonReader reader)
    {
        if (reader.ValueTextEquals("0"u8))
        {
            return 0;
        }

        if (reader.ValueTextEquals("1"u8))
        {
            return 1;
        }

        return reader.ValueTextEquals("2"u8) ? 2 : -1;
    }
}
