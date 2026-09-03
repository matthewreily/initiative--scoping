using InitiativeScoping.Infrastructure;
using InitiativeScoping.Infrastructure.Persistence;
using InitiativeScoping.Web.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddAppAuth(builder.Configuration, builder.Environment);
builder.Services.AddControllersWithViews();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = builder.Configuration.GetValue<long?>("Limits:MaxRequestBodyBytes") ?? 12 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = builder.Configuration.GetValue<long?>("Limits:MaxRequestBodyBytes") ?? 12 * 1024 * 1024);
builder.Services.AddHealthChecks().AddDbContextCheck<AppDbContext>();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // Behind a managed proxy (Cloud Run / load balancer) the peer is not a known IP.
    o.KnownNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

var migrateOnly = args.Contains("--migrate");
if (migrateOnly || app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    // Migrations target PostgreSQL; the SQLite dev provider uses schema-from-model.
    if (db.Database.IsNpgsql())
    {
        await db.Database.MigrateAsync();
    }
    else
    {
        await db.Database.EnsureCreatedAsync();
    }

    if (app.Configuration.GetValue<bool>("Database:SeedOnStartup"))
    {
        await DbSeeder.SeedAsync(db);
    }

    if (migrateOnly)
    {
        return;
    }
}

if (app.Configuration.GetValue<bool>("ForwardedHeaders:Enabled"))
{
    app.UseForwardedHeaders();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");
app.Use((context, next) =>
{
    var headers = context.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    headers.ContentSecurityPolicy = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; frame-ancestors 'none'; form-action 'self' https://login.microsoftonline.com";
    return next();
});

var culture = new CultureInfo(app.Configuration["Culture"] ?? "en-US");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture]
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseSerilogRequestLogging();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=BusinessUnits}/{action=Index}/{id?}");
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
if (!app.Configuration.GetValue<bool>("Auth:UseDevelopmentAuth"))
{
    app.MapRazorPages();
}

app.Run();

public partial class Program;
