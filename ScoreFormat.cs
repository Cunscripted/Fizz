using System.Globalization;

/// <summary>
/// Formatting helper for values that can legitimately be negative (bonuses, drains,
/// curses, "wears out" vs "grows" drift, etc). Never hardcode "+{value}" directly in a
/// string - for a negative value that literally produces "+-5" instead of "-5", since
/// C#'s string interpolation doesn't strip signs. Use Signed() instead everywhere a
/// value might go negative.
/// </summary>
public static class ScoreFormat
{
    /// <summary>"+5", "-5", or "0" - adds a "+" only for non-negative values; a negative value already carries its own "-" from ToString.</summary>
    public static string Signed(float value, string numberFormat = "0.#")
    {
        string s = value.ToString(numberFormat, CultureInfo.InvariantCulture);
        return value >= 0f ? "+" + s : s;
    }

    /// <summary>Int overload of Signed(float, string).</summary>
    public static string Signed(int value)
    {
        return value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
    }
}