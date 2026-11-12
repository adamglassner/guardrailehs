using Guardrail.Data;

namespace Guardrail.Services;

/// <summary>
/// Standard US safety rates for a period, from Incidents and Monthly Data:
/// OSHA rates are per 200,000 hours (100 full-time workers for a year); the vehicle accident
/// frequency rate is per 1,000,000 miles.
/// </summary>
public sealed class SafetyMetrics
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public string? Location { get; init; }

    public List<Incident> Incidents { get; init; } = [];
    public decimal Hours { get; init; }
    public decimal Miles { get; init; }
    /// <summary>Average monthly headcount over the months entered (OSHA 300A "annual average number of employees").</summary>
    public decimal AverageEmployees { get; init; }
    /// <summary>Vehicle accidents from other sources, added to the count.</summary>
    public int ExtraVehicleAccidents { get; init; }
    /// <summary>Accidents in the Fleet Hub accident register during the period.</summary>
    public int FleetAccidents { get; init; }
    /// <summary>Once the accident register is in use it is the source for vehicle accidents (avoids counting a crash twice).</summary>
    public bool UseFleetRegister { get; init; }

    public int Recordables => Incidents.Count(i => i.IsRecordable);
    public int DartCases => Incidents.Count(i => i.IsDart);
    public int LostTimeCases => Incidents.Count(i => i.IsLostTime);
    public int DaysAway => Incidents.Where(i => i.IsInjuryIllness).Sum(i => i.DaysAway);
    public int DaysRestricted => Incidents.Where(i => i.IsInjuryIllness).Sum(i => i.DaysRestricted);
    public int VehicleAccidents => (UseFleetRegister ? FleetAccidents : Incidents.Count(i => i.IsVehicle)) + ExtraVehicleAccidents;

    public decimal? Trir => PerHours(Recordables);
    public decimal? Dart => PerHours(DartCases);
    public decimal? Ltir => PerHours(LostTimeCases);
    public decimal? LostDaysRate => PerHours(DaysAway);
    public decimal? RestrictedDaysRate => PerHours(DaysRestricted);
    public decimal? AccidentFrequencyRate => Miles > 0 ? VehicleAccidents * 1_000_000m / Miles : null;

    private decimal? PerHours(int count) => Hours > 0 ? count * 200_000m / Hours : null;

    public static string Format(decimal? rate) => rate is { } r ? r.ToString("0.00") : "—";

    public static bool SameLocation(string recordLocation, string? filter) =>
        filter is null || string.Equals(recordLocation.Trim(), filter, StringComparison.OrdinalIgnoreCase);

    /// <summary>Metrics for incidents dated <paramref name="from"/>..<paramref name="to"/> (inclusive) and the months they fall in.</summary>
    public static SafetyMetrics Compute(DateTime from, DateTime to, string? location = null, int extraVehicleAccidents = 0)
    {
        from = from.Date;
        to = to.Date;
        var firstMonth = MonthlyEntry.FirstOfMonth(from);
        var months = DataStore.Monthly.Items
            .Where(m => m.Month >= firstMonth && m.Month <= to && SameLocation(m.Location, location))
            .ToList();
        var perMonthHeadcount = months.GroupBy(m => m.Month).Select(g => g.Sum(m => m.Employees)).ToList();

        return new SafetyMetrics
        {
            From = from,
            To = to,
            Location = location,
            Incidents = DataStore.Incidents.Items
                .Where(i => i.Date.Date >= from && i.Date.Date <= to && SameLocation(i.Location, location))
                .ToList(),
            Hours = months.Sum(m => m.HoursWorked),
            Miles = months.Sum(m => m.MilesDriven),
            AverageEmployees = perMonthHeadcount.Count == 0 ? 0 : perMonthHeadcount.Average(),
            ExtraVehicleAccidents = extraVehicleAccidents,
            // The register records street / city, not your locations, so it's used for company-wide figures.
            UseFleetRegister = location is null && DataStore.FleetAccidents.Items.Count > 0,
            FleetAccidents = DataStore.FleetAccidents.Items.Count(a => a.Date.Date >= from && a.Date.Date <= to),
        };
    }

    public static SafetyMetrics ForYear(int year, string? location = null, int extraVehicleAccidents = 0) =>
        Compute(new DateTime(year, 1, 1), new DateTime(year, 12, 31), location, extraVehicleAccidents);
}
