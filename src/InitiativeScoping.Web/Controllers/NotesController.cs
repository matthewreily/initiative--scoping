using InitiativeScoping.Application;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Initiatives;
using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace InitiativeScoping.Web.Controllers;

/// <summary>Notes on an initiative or on one of its baseline versions.</summary>
[Authorize(Policy = AppPolicies.CanView)]
[AutoValidateAntiforgeryToken]
public class NotesController(AppDbContext db, ICurrentUser currentUser, IAuditLog audit, TimeProvider clock) : Controller
{
    public const int MaxBodyLength = 4000;

    [HttpPost("Initiatives/{id:int}/Notes")]
    public async Task<IActionResult> Add(int id, string? body, int? baselineId, string? returnUrl, CancellationToken ct)
    {
        if (!InitiativeAccess.CanAddNote(currentUser))
        {
            return Forbid();
        }

        var exists = await db.Initiatives.AnyAsync(i => i.Id == id, ct);
        if (!exists)
        {
            return NotFound();
        }

        var text = body?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return Back("Note cannot be empty.", id, returnUrl, success: false);
        }

        if (text.Length > MaxBodyLength)
        {
            return Back($"Note is too long (max {MaxBodyLength:N0} characters).", id, returnUrl, success: false);
        }

        ForecastBaseline? baseline = null;
        if (baselineId is not null)
        {
            baseline = await db.ForecastBaselines.FirstOrDefaultAsync(b => b.Id == baselineId && b.InitiativeId == id, ct);
            if (baseline is null)
            {
                return NotFound();
            }
        }

        var note = new InitiativeNote
        {
            InitiativeId = id,
            ForecastBaselineId = baseline?.Id,
            Body = text,
            CreatedBy = currentUser.UserId,
            CreatedAt = clock.GetUtcNow()
        };
        db.InitiativeNotes.Add(note);
        await db.SaveChangesAsync(ct);
        audit.Record(nameof(Initiative), id, AuditActions.NoteAdded, new { NoteId = note.Id, BaselineVersion = baseline?.Version, note.Body });
        await db.SaveChangesAsync(ct);
        return Back(baseline is null ? "Note added." : $"Note added to baseline v{baseline.Version}.", id, returnUrl, success: true);
    }

    [HttpPost("Initiatives/{id:int}/Notes/{noteId:int}/Delete")]
    public async Task<IActionResult> Delete(int id, int noteId, string? returnUrl, CancellationToken ct)
    {
        var note = await db.InitiativeNotes.Include(n => n.ForecastBaseline)
            .FirstOrDefaultAsync(n => n.Id == noteId && n.InitiativeId == id, ct);
        if (note is null)
        {
            return NotFound();
        }

        if (!InitiativeAccess.CanDeleteNote(currentUser, note))
        {
            return Forbid();
        }

        db.InitiativeNotes.Remove(note);
        audit.Record(nameof(Initiative), id, AuditActions.NoteDeleted, new { NoteId = note.Id, BaselineVersion = note.ForecastBaseline?.Version, note.CreatedBy, note.Body });
        await db.SaveChangesAsync(ct);
        return Back("Note deleted.", id, returnUrl, success: true);
    }

    private IActionResult Back(string message, int id, string? returnUrl, bool success)
    {
        TempData[success ? "Success" : "Error"] = message;
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        return RedirectToAction(nameof(InitiativesController.Details), "Initiatives", new { id }, fragment: "pane-notes");
    }
}
