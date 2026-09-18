using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace InitiativeScoping.Web;

/// <summary>
/// Field-level errors for an inline (non-full-page) form, carried across the redirect in TempData and
/// rendered by _Flash as JSON for site.js to apply: values are restored, invalid fields highlighted.
/// </summary>
public static class InlineFormErrors
{
    public const string Key = "InlineFormErrors";

    public static string Serialize(string formId, ModelStateDictionary modelState, IFormCollection form)
    {
        var errors = modelState
            .Where(kv => kv.Value is { Errors.Count: > 0 })
            .ToDictionary(kv => kv.Key, kv => kv.Value!.Errors.First().ErrorMessage is { Length: > 0 } m ? m : "Invalid value.");
        var values = form
            .Where(kv => kv.Key != "__RequestVerificationToken")
            .ToDictionary(kv => kv.Key, kv => kv.Value.LastOrDefault() ?? "");
        return JsonSerializer.Serialize(new { form = formId, errors, values });
    }
}
