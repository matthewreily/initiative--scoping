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
        var exported = new SynchronizedActivities();
        await using var factory = new WebAppFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(exported))));
        var client = factory.CreateClient();

        // Activity listeners are process-wide, so the exporter also sees spans from other test
        // factories running in parallel; correlate by trace id via an inbound traceparent header.
        var healthTrace = ActivityTraceId.CreateRandom();
        var pageTrace = ActivityTraceId.CreateRandom();
        (await client.SendAsync(WithTrace(HttpMethod.Get, "/health", healthTrace))).EnsureSuccessStatusCode();
        (await client.SendAsync(WithTrace(HttpMethod.Get, "/Initiatives", pageTrace))).EnsureSuccessStatusCode();
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var server = exported.Snapshot().Where(a => a.Kind == ActivityKind.Server).ToList();
        Assert.Contains(server, a => a.TraceId == pageTrace);
        Assert.DoesNotContain(server, a => a.TraceId == healthTrace);
    }

    private static HttpRequestMessage WithTrace(HttpMethod method, string path, ActivityTraceId traceId)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");
        return request;
    }

    [Fact]
    public async Task Actuals_import_emits_application_span_with_counts()
    {
        var exported = new SynchronizedActivities();
        await using var factory = new WebAppFactory().WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            services.ConfigureOpenTelemetryTracerProvider((_, tracing) => tracing.AddInMemoryExporter(exported))));
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var token = TokenRegex.Match(await client.GetStringAsync("/Actuals")).Groups[1].Value;
        var file = new StringContent("ExternalProjectId,ExternalPersonId,WorkDate,Hours\nPRJ-OTEL,PV-OTEL,2026-03-10,8\n");
        file.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" }, { file, "File", "otel.csv" } };
        var importTrace = ActivityTraceId.CreateRandom();
        var request = WithTrace(HttpMethod.Post, "/Actuals/Import", importTrace);
        request.Content = form;
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        factory.Services.GetRequiredService<TracerProvider>().ForceFlush();

        var import = Assert.Single(exported.Snapshot(), a => a.TraceId == importTrace
            && a.Source.Name == AppTelemetry.ActivitySourceName && a.DisplayName == "actuals.import");
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

    // The in-memory exporter appends from request threads; List<T> is not safe for that.
    private sealed class SynchronizedActivities : ICollection<Activity>
    {
        private readonly List<Activity> _items = new();

        public List<Activity> Snapshot()
        {
            lock (_items) return _items.ToList();
        }

        public void Add(Activity item)
        {
            lock (_items) _items.Add(item);
        }

        public int Count { get { lock (_items) return _items.Count; } }
        public bool IsReadOnly => false;
        public void Clear() { lock (_items) _items.Clear(); }
        public bool Contains(Activity item) { lock (_items) return _items.Contains(item); }
        public void CopyTo(Activity[] array, int arrayIndex) { lock (_items) _items.CopyTo(array, arrayIndex); }
        public bool Remove(Activity item) { lock (_items) return _items.Remove(item); }
        public IEnumerator<Activity> GetEnumerator() => Snapshot().GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
