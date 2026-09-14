using System.ComponentModel.DataAnnotations;
using InitiativeScoping.Application;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Enums;
using InitiativeScoping.Infrastructure.Access;
using InitiativeScoping.Web.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Landing page for signed-in people who have no app role yet: shows status and lets them ask for access.</summary>
[Authorize]
[AutoValidateAntiforgeryToken]
public class AccessController(UserAccessService access, AccessCache accessCache) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (AppRoles.All.Any(User.IsInRole))
        {
            return RedirectToAction("Index", "Home");
        }

        var account = await access.FindAsync(SignedIn(), ct);
        return View(new AccessViewModel { DisplayName = PrincipalClaims.DisplayName(User), Email = PrincipalClaims.Email(User), Account = account });
    }

    [HttpPost("/Access/Request")]
    public async Task<IActionResult> Submit(AccessRequestModel model, CancellationToken ct)
    {
        if (AppRoles.All.Any(User.IsInRole))
        {
            return RedirectToAction("Index", "Home");
        }

        var account = await access.RequestAccessAsync(SignedIn(), model.Note, ct);
        accessCache.Invalidate();
        TempData["Success"] = account.Status == UserAccountStatus.Pending
            ? "Your request has been sent to the administrators."
            : "Your account already exists.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Ends the app session and the Entra session, landing on <see cref="SignedOut"/>.</summary>
    [HttpPost("/Access/SignOut")]
    public IActionResult EndSession()
    {
        var landing = Url.Action(nameof(SignedOut))!;
        if (User.Identity?.AuthenticationType == AuthSetup.DevScheme)
        {
            return Redirect(landing);
        }

        return SignOut(
            new AuthenticationProperties { RedirectUri = landing },
            CookieAuthenticationDefaults.AuthenticationScheme,
            OpenIdConnectDefaults.AuthenticationScheme);
    }

    [AllowAnonymous]
    [HttpGet("/Access/SignedOut")]
    public IActionResult SignedOut() => View();

    private SignedInUser SignedIn() => new(PrincipalClaims.ObjectId(User), PrincipalClaims.Email(User), PrincipalClaims.DisplayName(User), []);
}

public class AccessViewModel
{
    public required string DisplayName { get; init; }
    public string? Email { get; init; }
    public UserAccount? Account { get; init; }
}

public class AccessRequestModel
{
    [StringLength(1000)]
    public string? Note { get; set; }
}
