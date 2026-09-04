using InitiativeScoping.Application;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace InitiativeScoping.Web.Telemetry;

public static class TelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry tracing and metrics. Signals are exported over OTLP only when
    /// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> (or <c>OpenTelemetry:Otlp:Endpoint</c>) is configured,
    /// so local development produces no network traffic unless a collector is running.
    /// </summary>
    public static IServiceCollection AddAppTelemetry(
        this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var section = configuration.GetSection("OpenTelemetry");
        if (!section.GetValue("Enabled", true))
        {
            return services;
        }

        var serviceName = section["ServiceName"] ?? "initiative-scoping";
        var serviceVersion = typeof(TelemetryExtensions).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        var otlpEndpoint = FirstNonBlank(section["Otlp:Endpoint"],
            Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT"));
        var exportEnabled = !string.IsNullOrWhiteSpace(otlpEndpoint);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(serviceName, serviceVersion: serviceVersion, serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment.EnvironmentName,
                    ["cloud.region"] = Environment.GetEnvironmentVariable("CLOUD_RUN_REGION") ?? "local",
                    ["service.namespace"] = "initiative-scoping",
                })
                .AddEnvironmentVariableDetector())
            .WithTracing(t =>
            {
                t.AddSource(AppTelemetry.ActivitySourceName)
                    .AddSource("Npgsql")
                    .AddAspNetCoreInstrumentation(o =>
                    {
                        o.RecordException = true;
                        o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();
                if (exportEnabled)
                {
                    t.AddOtlpExporter();
                }
            })
            .WithMetrics(m =>
            {
                m.AddMeter(AppTelemetry.MeterName)
                    .AddMeter("Npgsql")
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (exportEnabled)
                {
                    m.AddOtlpExporter();
                }
            });

        if (exportEnabled)
        {
            services.Configure<OpenTelemetry.Exporter.OtlpExporterOptions>(o => o.Endpoint = new Uri(otlpEndpoint!));
        }

        return services;
    }

    private static string? FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
