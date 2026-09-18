using InitiativeScoping.Web.Services;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace InitiativeScoping.Web.TagHelpers;

/// <summary>
/// <c>&lt;help for="forecast-cost" /&gt;</c> renders a small "?" button whose tooltip is the glossary text for that term.
/// The button has no text content so sortable/column-picker code that reads a header's text is unaffected.
/// </summary>
[HtmlTargetElement("help", Attributes = "for", TagStructure = TagStructure.WithoutEndTag)]
public sealed class HelpTagHelper : TagHelper
{
    [HtmlAttributeName("for")]
    public string For { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var term = HelpText.Find(For) ?? throw new InvalidOperationException($"Unknown help term '{For}'.");
        output.TagName = "button";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("type", "button");
        output.Attributes.SetAttribute("class", "help-hint");
        output.Attributes.SetAttribute("aria-label", $"What is {term.Label}?");
        output.Attributes.SetAttribute("data-help", term.Key);
        output.Attributes.SetAttribute("data-bs-toggle", "tooltip");
        output.Attributes.SetAttribute("data-bs-title", term.Text);
        output.Content.SetHtmlContent("");
    }
}
