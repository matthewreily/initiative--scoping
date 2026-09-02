using InitiativeScoping.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Policy = AppPolicies.Admin)]
[AutoValidateAntiforgeryToken]
public abstract class AdminControllerBase : Controller
{
    protected IActionResult RedirectWithSuccess(string message, string action = "Index", object? routeValues = null)
    {
        TempData["Success"] = message;
        return RedirectToAction(action, routeValues);
    }

    protected IActionResult RedirectWithError(string message, string action = "Index", object? routeValues = null)
    {
        TempData["Error"] = message;
        return RedirectToAction(action, routeValues);
    }
}
