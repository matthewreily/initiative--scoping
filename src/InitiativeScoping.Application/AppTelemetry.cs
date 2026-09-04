using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace InitiativeScoping.Application;

/// <summary>
/// Application-level OpenTelemetry sources. Instruments are cheap no-ops until a listener
/// (the OpenTelemetry SDK in the Web host) subscribes to them.
/// </summary>
public static class AppTelemetry
{
    public const string ActivitySourceName = "InitiativeScoping";
    public const string MeterName = "InitiativeScoping";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> ActualsImports = Meter.CreateCounter<long>(
        "initiative_scoping.actuals.imports", description: "Completed actuals import runs.");

    public static readonly Counter<long> ActualsRecords = Meter.CreateCounter<long>(
        "initiative_scoping.actuals.records", description: "Actual time entries ingested, by outcome.");

    public static readonly Counter<long> BaselinesCaptured = Meter.CreateCounter<long>(
        "initiative_scoping.baselines.captured", description: "Forecast baselines captured (activation and re-baseline).");

    public static readonly Counter<long> StatusChanges = Meter.CreateCounter<long>(
        "initiative_scoping.initiatives.status_changes", description: "Initiative status transitions.");
}
