using InitiativeScoping.Web.Services;

namespace InitiativeScoping.Integration.Tests;

public class HelpTests(WebAppFactory factory) : IClassFixture<WebAppFactory>
{
    [Fact]
    public void Glossary_keys_are_unique_and_every_term_has_text()
    {
        Assert.Equal(HelpText.Terms.Count, HelpText.Terms.Select(t => t.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(HelpText.Terms, t => { Assert.False(string.IsNullOrWhiteSpace(t.Label)); Assert.False(string.IsNullOrWhiteSpace(t.Text)); });
        Assert.NotNull(HelpText.Find("EAC"));
        Assert.Null(HelpText.Find("nope"));
    }

    [Fact]
    public async Task Help_page_lists_every_glossary_term_and_offers_the_tour()
    {
        var page = await factory.CreateClient().GetStringAsync("/Home/Help");
        Assert.All(HelpText.Terms, t => Assert.Contains($"id=\"term-{t.Key}\"", page));
        Assert.Contains("data-tour-start=\"welcome\"", page);
        Assert.Contains("data-shortcut-help", page);
    }

    [Fact]
    public async Task Metric_hints_render_glossary_text_as_tooltips_and_pages_declare_their_tour()
    {
        var client = factory.CreateClient();

        var home = await client.GetStringAsync("/");
        Assert.Contains("<body data-tour=\"welcome\">", home);
        Assert.Contains("data-tour=\"welcome:1\"", home);
        Assert.Contains("data-tour=\"welcome:2\"", home);
        Assert.Contains("href=\"/Home/Help\"", home);

        var portfolio = await client.GetStringAsync("/Portfolio");
        var budget = HelpText.Find("budget")!;
        Assert.Contains($"data-help=\"budget\" data-bs-toggle=\"tooltip\" data-bs-title=\"{budget.Text}\"", portfolio);
        Assert.Contains("aria-label=\"What is Variance?\"", portfolio);
        Assert.DoesNotContain("data-tour=\"welcome:1\"", portfolio);
        Assert.DoesNotContain("<body data-tour=\"welcome\">", portfolio);
    }
}
