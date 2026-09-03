using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Infrastructure.Actuals;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InitiativeScoping.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "SqlServer";
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
                options.UseSqlServer(connectionString, x => x.MigrationsAssembly("InitiativeScoping.Infrastructure"));
            }
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLog, DbAuditLog>();
        services.AddScoped<IActualsImporter, ActualsImporter>();

        return services;
    }
}
