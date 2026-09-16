using System.Globalization;

namespace InitiativeScoping.Web;

public static class Money
{
    /// <summary>"$1.2M" / "$850K" / "$420" — for dashboard tiles; pair with the full value in a title attribute.</summary>
    public static string Compact(decimal value, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        var abs = Math.Abs(value);
        var sign = value < 0 ? "-" : "";
        var symbol = culture.NumberFormat.CurrencySymbol;
        if (abs >= 1_000_000_000m) return $"{sign}{symbol}{Scale(abs / 1_000_000_000m)}B";
        if (abs >= 1_000_000m) return $"{sign}{symbol}{Scale(abs / 1_000_000m)}M";
        if (abs >= 10_000m) return $"{sign}{symbol}{Scale(abs / 1_000m)}K";
        return value.ToString("C0", culture);

        static string Scale(decimal scaled) => scaled.ToString(scaled < 10m ? "0.#" : "0", CultureInfo.InvariantCulture);
    }
}
