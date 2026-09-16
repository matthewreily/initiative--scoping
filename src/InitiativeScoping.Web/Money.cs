using System.Globalization;

namespace InitiativeScoping.Web;

public static class Money
{
    private static readonly (decimal Divisor, string Suffix)[] Units =
    {
        (1_000_000_000m, "B"),
        (1_000_000m, "M"),
        (1_000m, "K")
    };

    /// <summary>"$1.2M" / "$850K" / "$420" — for dashboard tiles; pair with the full value in a title attribute.</summary>
    public static string Compact(decimal value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var abs = Math.Abs(value);
        if (abs < 10_000m) return value.ToString("C0", culture);

        var unit = Array.FindIndex(Units, u => abs >= u.Divisor);
        var scaled = Round(abs / Units[unit].Divisor);
        if (scaled >= 1000m && unit > 0)
        {
            unit--;
            scaled = Round(abs / Units[unit].Divisor);
        }

        var decimals = scaled < 10m && scaled != decimal.Truncate(scaled) ? 1 : 0;
        var formatted = (value < 0 ? -scaled : scaled).ToString("C" + decimals, culture);
        return InsertSuffix(formatted, Units[unit].Suffix, culture);
    }

    private static decimal Round(decimal scaled) =>
        scaled < 10m ? Math.Round(scaled, 1, MidpointRounding.AwayFromZero) : Math.Round(scaled, 0, MidpointRounding.AwayFromZero);

    private static string InsertSuffix(string formatted, string suffix, CultureInfo culture)
    {
        var digits = culture.NumberFormat.NativeDigits;
        var lastDigit = -1;
        for (var i = formatted.Length - 1; i >= 0; i--)
        {
            if (char.IsDigit(formatted[i]) || Array.IndexOf(digits, formatted[i].ToString()) >= 0)
            {
                lastDigit = i;
                break;
            }
        }
        return lastDigit < 0 ? formatted + suffix : formatted.Insert(lastDigit + 1, suffix);
    }
}
