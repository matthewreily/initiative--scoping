using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Areas.Admin.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Areas.Admin.Controllers;

public class ApplicationPlatformsController(AppDbContext db, IAuditLog audit) : AdminControllerBase
{
    public async Task<IActionResult> Index(CancellationToken ct) => View(await db.ApplicationPlatforms.OrderBy(x => x.Name).ToListAsync(ct));
    public IActionResult Create() => View("Edit", new ApplicationPlatformEditModel());
    public async Task<IActionResult> Edit(int id, CancellationToken ct)
    {
        var app = await db.ApplicationPlatforms.FindAsync([id], ct);
        return app is null ? NotFound() : View(new ApplicationPlatformEditModel { Id = id, Name = app.Name, IsActive = app.IsActive });
    }

    [HttpPost]
    public Task<IActionResult> Create(ApplicationPlatformEditModel model, CancellationToken ct) => Save(0, model, ct);
    [HttpPost]
    public Task<IActionResult> Edit(int id, ApplicationPlatformEditModel model, CancellationToken ct) => Save(id, model, ct);

    private async Task<IActionResult> Save(int id, ApplicationPlatformEditModel model, CancellationToken ct)
    {
        model.Name ??= "";
        model.Id = id;
        var app = id == 0 ? new ApplicationPlatform { Name = "" } : await db.ApplicationPlatforms.FindAsync([id], ct);
        if (app is null) return NotFound();
        if (await db.ApplicationPlatforms.AnyAsync(x => x.Id != id && x.Name.ToLower() == model.Name.Trim().ToLower(), ct))
            ModelState.AddModelError(nameof(model.Name), "An application/platform with this name already exists.");
        if (!ModelState.IsValid) return View("Edit", model);
        var before = new { app.Name, app.IsActive };
        app.Name = model.Name.Trim();
        app.IsActive = model.IsActive;
        if (id == 0) db.ApplicationPlatforms.Add(app);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(ApplicationPlatform), app.Id, id == 0 ? AuditActions.Create : AuditActions.Update, new { Before = before, After = new { app.Name, app.IsActive } });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Application/platform saved.");
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var app = await db.ApplicationPlatforms.FindAsync([id], ct);
        if (app is null) return NotFound();
        if (await db.PodApplications.AnyAsync(x => x.ApplicationPlatformId == id, ct) || await db.InitiativeApplications.AnyAsync(x => x.ApplicationPlatformId == id, ct))
            return RedirectWithError("Application/platform is referenced; deactivate it instead.");
        db.ApplicationPlatforms.Remove(app);
        audit.Record(nameof(ApplicationPlatform), id, AuditActions.Delete, new { app.Name });
        await db.SaveChangesAsync(ct);
        return RedirectWithSuccess("Application/platform deleted.");
    }
}
