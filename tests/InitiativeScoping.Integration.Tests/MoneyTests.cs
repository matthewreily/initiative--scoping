using System.Globalization;
using InitiativeScoping.Web;

namespace InitiativeScoping.Integration.Tests;

public class MoneyTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(0, "$0")]
    [InlineData(420, "$420")]
    [InlineData(9_999, "$9,999")]
    [InlineData(12_500, "$13K")]
    [InlineData(850_000, "$850K")]
    [InlineData(999_499, "$999K")]
    [InlineData(999_950, "$1M")]
    [InlineData(999_999, "$1M")]
    [InlineData(1_234_567, "$1.2M")]
    [InlineData(9_950_000, "$10M")]
    [InlineData(999_950_000, "$1B")]
    [InlineData(999_999_999, "$1B")]
    [InlineData(3_200_000_000, "$3.2B")]
    public void Compact_en_US(decimal value, string expected)
        => Assert.Equal(expected, Money.Compact(value, EnUs));

    [Fact]
    public void Compact_negative_uses_culture_negative_pattern()
        => Assert.Equal("-$2.5M", Money.Compact(-2_500_000m, EnUs));

    [Fact]
    public void Compact_suffix_currency_culture_keeps_symbol_placement()
    {
        var fr = CultureInfo.GetCultureInfo("fr-FR");
        var compact = Money.Compact(1_200_000m, fr);
        var exact = 1.2m.ToString("C1", fr);
        Assert.Equal(exact.Replace("1,2", "1,2M"), compact);
        Assert.EndsWith("€", compact.Trim());
    }
}
