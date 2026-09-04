using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using InitiativeScoping.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace InitiativeScoping.Integration.Tests;

public class TelemetryTests
{
    private static readonly Regex TokenRegex = new("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"", RegexOptions.Compiled);

    [Fact]
    public async Task Requests_produce_server_spans_and_health_is_filtered()
    {
        var exported = new List<Activity>();
        await using var factory = new WebAppFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(exported))));
        var client = factory.CreateClient();

        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        (await client.GetAsync("/Initiatives")).EnsureSuccessStatusCode();
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var server = exported.Where(a => a.Kind == ActivityKind.Server).ToList();
        Assert.Contains(server, a => a.DisplayName.Contains("Initiatives", StringComparison.OrdinalIgnoreCase)
                                     || a.GetTagItem("url.path")?.ToString() == "/Initiatives");
        Assert.DoesNotContain(server, a => a.GetTagItem("url.path")?.ToString() == "/health");
    }

    [Fact]
    public async Task Actuals_import_emits_application_span_with_counts()
    {
        var exported = new List<Activity>();
        await using var factory = new WebAppFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(exported))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = TokenRegex.Match(await client.GetStringAsync("/Actuals")).Groups[1].Value;
        var file = new StringContent("ExternalProjectId,ExternalPersonId,WorkDate,Hours\nPRJ-OTEL,PV-OTEL,2026-03-10,8\n");
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { file, "File", "otel.csv" } };
        var response = await client.PostAsync("/Actuals/Import", form);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var import = Assert.Single(exported, a => a.Source.Name == AppTelemetry.ActivitySourceName && a.DisplayName == "actuals.import");
        Assert.Equal("Csv", import.GetTagItem("actuals.source"));
        Assert.Equal(1, import.GetTagItem("actuals.records"));
        Assert.Equal(1, import.GetTagItem("actuals.unmapped"));
        Assert.NotNull(import.Parent);
        Assert.Equal(ActivityKind.Server, import.Parent!.Kind);
    }

    [Fact]
    public async Task Telemetry_can_be_disabled_by_configuration()
    {
        await using var factory = new WebAppFactory().WithWebHostBuilder(b => b.UseSetting("OpenTelemetry:Enabled", "false"));
        var client = factory.CreateClient();

        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        Assert.Null(factory.Services.GetService<TracerProvider>());
    }
}
