using Google.Cloud.Kms.V1;
using InitiativeScoping.Application.Abstractions;
using InitiativeScoping.Application.Exports;
using InitiativeScoping.Infrastructure.Actuals;
using InitiativeScoping.Infrastructure.DataProtection;
using InitiativeScoping.Infrastructure.Exports;
using InitiativeScoping.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        // Keys at rest are wrapped with a Cloud KMS key when one is configured (production);
        // local/dev runs without KMS store them unwrapped.
        var kmsKeyName = configuration["DataProtection:KmsKeyName"];
        if (!string.IsNullOrWhiteSpace(kmsKeyName))
        {
            var keyName = CryptoKeyName.Parse(kmsKeyName);
            services.TryAddSingleton(_ => KeyManagementServiceClient.Create());
            services.AddSingleton<IConfigureOptions<KeyManagementOptions>>(sp =>
                new ConfigureOptions<KeyManagementOptions>(o =>
                    o.XmlEncryptor = new KmsXmlEncryptor(sp.GetRequiredService<KeyManagementServiceClient>(), keyName)));
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAuditLog, DbAuditLog>();
        services.AddScoped<IWorkCalendar, DbWorkCalendar>();
        services.AddScoped<IActualsImporter, ActualsImporter>();
        services.AddSingleton<IExportWriter, CsvExportWriter>();
        services.AddSingleton<IExportWriter, XlsxExportWriter>();

        return services;
    }
}
