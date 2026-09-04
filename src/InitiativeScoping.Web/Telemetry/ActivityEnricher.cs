using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace InitiativeScoping.Web.Telemetry;

/// <summary>Adds the current OpenTelemetry trace/span ids to every log event so logs correlate with traces.</summary>
public sealed class ActivityEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
    }
}
