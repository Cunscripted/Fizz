using System;
using System.Globalization;

/// <summary>
/// Formatting helpers for scores and other values.
///
/// Big(): scores can grow far past what an int holds (about 2.1 billion - beyond that an int
/// wraps around to NEGATIVE numbers). Scores are kept as doubles (good to ~1e308) and shown
/// normally below BigThreshold (1e8 = 100,000,000), then as a short mantissa + exponent above it,
/// e.g. 123,456,789 -> "1.23e8", 4.5 trillion -> "4.5e12".
///
/// Signed(): for values that can legitimately be negative (bonuses, drains, drift). Never hardcode
/// "+{value}" in a string - a negative value would print "+-5" - use Signed() instead.
/// </summary>
public static class ScoreFormat
{
    /// <summary>At or above this (in absolute value) numbers switch to "1.23e8" style.</summary>
    public const double BigThreshold = 1e8;

    /// <summary>Digits kept after the decimal point in "1.23e8" style.</summary>
    public static int BigDecimals = 2;

    /// <summary>
    /// A score/number for display: normal formatting (smallFormat, e.g. "N0" = 12,345,678) below 1e8,
    /// "1.23e8" style above it. Handles negatives, and infinity (shown as "∞") if a score ever gets
    /// that absurdly large.
    /// </summary>
    public static string Big(double value, string smallFormat = "N0")
    {
        if (double.IsNaN(value)) return "0";
        if (double.IsInfinity(value)) return value > 0 ? "∞" : "-∞";

        double abs = Math.Abs(value);
        if (abs < BigThreshold)
        {
            // Round first so "N0" etc. never shows e.g. 99,999,999.6 as 100,000,000 while being under 1e8.
            try { return Math.Round(value, 2).ToString(string.IsNullOrEmpty(smallFormat) ? "N0" : smallFormat); }
            catch (FormatException) { return Math.Round(value).ToString("N0"); }
        }

        int exponent = (int)Math.Floor(Math.Log10(abs));
        double mantissa = abs / Math.Pow(10, exponent);
        double scale = Math.Pow(10, BigDecimals);
        mantissa = Math.Floor(mantissa * scale) / scale; // truncate, don't round up past what was actually scored
        if (mantissa >= 10) { mantissa /= 10; exponent++; }

        string m = mantissa.ToString("0." + new string('#', Math.Max(0, BigDecimals)), CultureInfo.InvariantCulture);
        return (value < 0 ? "-" : "") + m + "e" + exponent.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>"+5", "-5", or "0" - adds a "+" only for non-negative values. Huge values use "1.23e8" style.</summary>
    public static string Signed(double value, string numberFormat = "0.#")
    {
        string s = Math.Abs(value) >= BigThreshold
            ? Big(value)
            : value.ToString(numberFormat, CultureInfo.InvariantCulture);
        return value >= 0 ? "+" + s : s;
    }

    /// <summary>Float overload of Signed(double, string) - kept so existing float calls stay unambiguous.</summary>
    public static string Signed(float value, string numberFormat = "0.#") => Signed((double)value, numberFormat);

    /// <summary>Int overload of Signed(double, string).</summary>
    public static string Signed(int value)
    {
        return value >= 0 ? "+" + value.ToString(CultureInfo.InvariantCulture) : value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Clamps a big score into an int for old APIs/events that still take ints (never wraps negative).</summary>
    public static int ClampToInt(double value)
    {
        if (double.IsNaN(value)) return 0;
        if (value >= int.MaxValue) return int.MaxValue;
        if (value <= int.MinValue) return int.MinValue;
        return (int)Math.Round(value);
    }
}