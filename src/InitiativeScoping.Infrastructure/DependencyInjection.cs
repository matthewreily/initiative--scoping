using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Exports;
using InitiativeScoping.Infrastructure.Actuals;
using InitiativeScoping.Infrastructure.Exports;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InitiativeScoping.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "PostgreSql";
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connectionString, x => x.MigrationsAssembly("InitiativeScoping.Infrastructure"));
            }
            else
            {
                options.UseNpgsql(connectionString, x => x.MigrationsAssembly("InitiativeScoping.Infrastructure"));
            }
        });

        // Cookies (auth, antiforgery, TempData) must decrypt on any instance/revision, so keys live in the DB, not the container FS.
        services.AddDataProtection()
            .SetApplicationName("InitiativeScoping")
            .PersistKeysToDbContext<AppDbContext>();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLog, DbAuditLog>();
        services.AddScoped<IActualsImporter, ActualsImporter>();
        services.AddSingleton<IExportWriter, CsvExportWriter>();
        services.AddSingleton<IExportWriter, XlsxExportWriter>();

        return services;
    }
}
