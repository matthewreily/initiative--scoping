using InitiativeScoping.Domain.Entities;
using InitiativeScoping.Domain.Services;

namespace InitiativeScoping.Domain.Tests;

public class DurationCalculatorTests
{
    private static readonly IReadOnlySet<DateOnly> NoHolidays = new HashSet<DateOnly>();

    [Fact]
    public void Working_days_exclude_weekends_and_holidays()
    {
        // Mon 2026-01-05 .. Fri 2026-01-16 = 10 weekdays
        var start = new DateOnly(2026, 1, 5);
        var end = new DateOnly(2026, 1, 16);
        Assert.Equal(10, DurationCalculator.WorkingDays(start, end, NoHolidays));
        Assert.Equal(9, DurationCalculator.WorkingDays(start, end, new HashSet<DateOnly> { new(2026, 1, 12) }));
        // Weekend-only holiday does not double count
        Assert.Equal(10, DurationCalculator.WorkingDays(start, end, new HashSet<DateOnly> { new(2026, 1, 10) }));
        Assert.Equal(0, DurationCalculator.WorkingDays(end, start, NoHolidays));
        Assert.Equal(0, DurationCalculator.WorkingDays(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 11), NoHolidays));
    }

    [Fact]
    public void Hours_and_percent_round_trip()
    {
        Assert.Equal(80m, DurationCalculator.Hours(100m, 10, 8m));
        Assert.Equal(30m, DurationCalculator.Hours(50m, 10, 6m));
        Assert.Equal(26.66m, DurationCalculator.Hours(33.33m, 10, 8m));
        Assert.Equal(50m, DurationCalculator.PercentFor(40m, 10, 8m));
        Assert.Equal(0m, DurationCalculator.PercentFor(40m, 0, 8m));
        Assert.Equal(12.5m, DurationCalculator.PercentFor(10m, 10, 8m));
        // Hours are per person; quantity multiplies downstream (ForecastCalculator).
        var phase = new Phase { Name = "Build", PlannedStart = new DateOnly(2026, 1, 5), PlannedEnd = new DateOnly(2026, 1, 16) };
        var allocation = new InitiativeAllocation { Location = "Onshore", Quantity = 3, AllocationPercent = 50m };
        Assert.Equal(40m, DurationCalculator.Hours(allocation, phase, NoHolidays, 8m));
    }

    [Fact]
    public void Split_window_is_contiguous_proportional_and_covers_range()
    {
        var start = new DateOnly(2026, 3, 2);
        var end = new DateOnly(2026, 4, 30); // 60 days

        var parts = DurationCalculator.SplitWindow(start, end, [20m, 70m, 10m]);

        Assert.Equal(3, parts.Count);
        Assert.Equal(start, parts[0].Start);
        Assert.Equal(end, parts[2].End);
        Assert.Equal(12, parts[0].End.DayNumber - parts[0].Start.DayNumber + 1);
        Assert.Equal(42, parts[1].End.DayNumber - parts[1].Start.DayNumber + 1);
        Assert.Equal(6, parts[2].End.DayNumber - parts[2].Start.DayNumber + 1);
        for (var i = 1; i < parts.Count; i++)
        {
            Assert.Equal(parts[i - 1].End.AddDays(1), parts[i].Start);
        }
    }

    [Fact]
    public void Split_window_gives_every_phase_at_least_one_day()
    {
        var start = new DateOnly(2026, 3, 2);
        var parts = DurationCalculator.SplitWindow(start, start.AddDays(2), [98m, 1m, 1m]);
        Assert.All(parts, p => Assert.True(p.End >= p.Start));
        Assert.Equal(start.AddDays(2), parts[2].End);
    }

    [Fact]
    public void Tiling_validation_reports_gaps_overlaps_and_edges()
    {
        var start = new DateOnly(2026, 1, 1);
        var end = new DateOnly(2026, 1, 31);
        Phase P(string n, int seq, int s, int e) => new() { Name = n, Sequence = seq, PlannedStart = new(2026, 1, s), PlannedEnd = new(2026, 1, e) };

        Assert.Null(DurationCalculator.ValidateTiling(start, end, [P("A", 1, 1, 10), P("B", 2, 11, 31)]));
        Assert.Null(DurationCalculator.ValidateTiling(start, end, []));
        Assert.Contains("First phase", DurationCalculator.ValidateTiling(start, end, [P("A", 1, 2, 31)]));
        Assert.Contains("'B' must start on 2026-01-11", DurationCalculator.ValidateTiling(start, end, [P("A", 1, 1, 10), P("B", 2, 12, 31)]));
        Assert.Contains("'B' must start on 2026-01-11", DurationCalculator.ValidateTiling(start, end, [P("A", 1, 1, 10), P("B", 2, 10, 31)]));
        Assert.Null(DurationCalculator.ValidateTiling(start, end, [P("A", 1, 1, 30)]));
        Assert.Contains("Last phase", DurationCalculator.ValidateTiling(start, end, [P("A", 1, 1, 30)], requireFullCoverage: true));
        Assert.Contains("ends after", DurationCalculator.ValidateTiling(start, new DateOnly(2026, 1, 20), [P("A", 1, 1, 30)]));
        Assert.Contains("ends before it starts", DurationCalculator.ValidateTiling(start, end, [P("A", 1, 10, 1)]));
    }
}
