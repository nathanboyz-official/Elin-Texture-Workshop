using System.Globalization;

namespace ElinTextureManager.Core.Pcc;

/// <summary>
/// The colour maths behind the dye wheel.
///
/// Kept out of the view because it is arithmetic, and arithmetic is worth pinning down:
/// a wheel that drifts by a degree of hue on every round trip is a wheel that slowly
/// changes a character while the user only looks at it.
/// </summary>
public static class PccColour
{
    /// <summary>Six hex digits, upper case, as the game writes them.</summary>
    public static string ToHex(byte r, byte g, byte b) => $"{r:X2}{g:X2}{b:X2}";

    public static (byte R, byte G, byte B)? FromHex(string? hex)
    {
        if (hex is null) return null;

        var text = hex.TrimStart('#').Trim();
        if (text.Length != 6) return null;

        return byte.TryParse(text[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
               && byte.TryParse(text[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
               && byte.TryParse(text[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? (r, g, b)
            : null;
    }

    /// <summary>
    /// Hue in degrees, saturation and value from 0 to 1.
    /// </summary>
    public static (byte R, byte G, byte B) FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var c = value * saturation;
        var x = c * (1 - Math.Abs(hue / 60 % 2 - 1));
        var m = value - c;

        var (r, g, b) = hue switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };

        return ((byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
    }

    public static (double Hue, double Saturation, double Value) ToHsv(byte r, byte g, byte b)
    {
        var red = r / 255.0;
        var green = g / 255.0;
        var blue = b / 255.0;

        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        // Grey has no hue to speak of; reporting zero keeps the wheel marker still
        // rather than letting it jump about while the brightness slider moves.
        var hue = delta <= 0 ? 0
            : max == red ? 60 * (((green - blue) / delta % 6 + 6) % 6)
            : max == green ? 60 * ((blue - red) / delta + 2)
            : 60 * ((red - green) / delta + 4);

        return (hue, max <= 0 ? 0 : delta / max, max);
    }

    /// <summary>
    /// A colour that looks like it belongs in Elin.
    ///
    /// Not a random RGB triple, which comes out as neon and looks nothing like the game.
    /// The ranges are measured from the palette Elin itself uses - 92847B, 7C8FAE,
    /// 6F6E64, 9E824D and the rest sit between about 0.10 and 0.51 saturation and 0.38
    /// and 0.68 value. Widened slightly at both ends so the button is worth pressing
    /// twice, but still in the same family of muted tones.
    /// </summary>
    public static string RandomHex(Random random)
    {
        var hue = random.NextDouble() * 360;
        var saturation = 0.08 + random.NextDouble() * 0.47;
        var value = 0.35 + random.NextDouble() * 0.45;

        var (r, g, b) = FromHsv(hue, saturation, value);
        return ToHex(r, g, b);
    }
}
