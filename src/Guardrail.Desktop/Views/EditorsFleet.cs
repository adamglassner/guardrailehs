using System.IO;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Editors for the Fleet Hub: drivers, vehicles, DVIRs, maintenance, hours of service and the accident register.</summary>
public static partial class Editors
{
    // ------------------------------------------------------------------ Driver Qualification

    public static Driver? EditDriver(Driver? existing)
    {
        var d = existing ?? new Driver();
        bool isNew = existing is null;
        string? currentFile = d.HasFile ? Path.Combine(DataStore.Folder, d.StoredFile) : null;

        var dialog = new FormDialog(
            isNew ? "Add a driver" : $"Edit {d.Name}",
            isNew ? "Everything a driver qualification (DQ) file needs: license, medical certificate, annual MVR review and road test."
                  : $"{d.Number} · added {d.CreatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "name", Label = "Driver name", Required = true, Value = d.Name },
                new FormField { Key = "emp", Label = "Employee ID", Value = d.EmployeeId },
                new FormField { Key = "location", Label = "Location / terminal", Value = d.Location },
                new FormField { Key = "status", Label = "Application status", Kind = FieldKind.Choice, Options = Driver.ApplicationStatuses, Value = d.ApplicationStatus,
                                Hint = "Accepted drivers are the ones checked for DOT compliance." },
                new FormField { Key = "class", Label = "License class", Kind = FieldKind.Choice, Options = Driver.LicenseClasses, Value = d.LicenseClass },
                new FormField { Key = "licno", Label = "License number", Value = d.LicenseNumber },
                new FormField { Key = "state", Label = "License state", Value = d.LicenseState },
                new FormField { Key = "endorse", Label = "Endorsements", Value = d.Endorsements, Hint = "For example: H, N, T, X." },
                new FormField { Key = "licexp", Label = "License expires", Kind = FieldKind.Date, Value = d.LicenseExpires },
                new FormField { Key = "medexp", Label = "Medical certificate expires", Kind = FieldKind.Date, Value = d.MedicalCardExpires },
                new FormField { Key = "mvr", Label = "Last annual MVR review", Kind = FieldKind.Date, Value = d.LastMvrReview, Hint = "The next one is due 12 months later (49 CFR 391.25)." },
                new FormField { Key = "road", Label = "Road test (or equivalent) on file?", Kind = FieldKind.Choice, Options = Driver.YesNo, Value = d.RoadTest },
                new FormField { Key = "hire", Label = "Hire date", Kind = FieldKind.Date, Value = d.HireDate },
                new FormField { Key = "file", Label = "DQ file document (optional)", Kind = FieldKind.File, Value = currentFile },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = d.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add driver" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Drivers.Delete(d);
            TryDeleteFolder(Path.Combine(DataStore.Folder, "drivers", d.Id.ToString("N")));
            Toaster.Show($"{d.Name} removed.");
            return null;
        }

        if (!TryStoreChosenFile("drivers", d.Id, dialog.Text("file"), currentFile, f => d.StoredFile = f)) return null;
        d.Name = dialog.Text("name");
        d.EmployeeId = dialog.Text("emp");
        d.Location = dialog.Text("location");
        d.ApplicationStatus = dialog.Text("status");
        d.LicenseClass = dialog.Text("class");
        d.LicenseNumber = dialog.Text("licno");
        d.LicenseState = dialog.Text("state");
        d.Endorsements = dialog.Text("endorse");
        d.LicenseExpires = dialog.Date("licexp");
        d.MedicalCardExpires = dialog.Date("medexp");
        d.LastMvrReview = dialog.Date("mvr");
        d.RoadTest = dialog.Text("road");
        d.HireDate = dialog.Date("hire");
        d.Notes = dialog.Text("notes");

        if (isNew) DataStore.Drivers.Add(d); else DataStore.Drivers.Update(d);
        Toaster.Show($"{d.Name} saved: {d.DisplayStatus.ToLowerInvariant()}.");
        return d;
    }

    // ------------------------------------------------------------------ Vehicles

    public static Vehicle? EditVehicle(Vehicle? existing)
    {
        var v = existing ?? new Vehicle();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Add a vehicle" : $"Edit unit {v.UnitNumber}",
            isNew ? "Trucks, trailers, vans and equipment, with registration and the annual DOT inspection." : $"{v.Number} · added {v.CreatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "unit", Label = "Unit number", Required = true, Value = v.UnitNumber },
                new FormField { Key = "type", Label = "Type", Kind = FieldKind.Choice, Options = Vehicle.Types, Value = v.Type },
                new FormField { Key = "year", Label = "Year", Value = v.Year },
                new FormField { Key = "make", Label = "Make", Value = v.Make },
                new FormField { Key = "model", Label = "Model", Value = v.Model },
                new FormField { Key = "vin", Label = "VIN", Value = v.Vin },
                new FormField { Key = "plate", Label = "Plate", Value = v.Plate },
                new FormField { Key = "pstate", Label = "Plate state", Value = v.PlateState },
                new FormField { Key = "reg", Label = "Registration expires", Kind = FieldKind.Date, Value = v.RegistrationExpires },
                new FormField { Key = "annual", Label = "Last annual DOT inspection", Kind = FieldKind.Date, Value = v.LastAnnualInspection,
                                Hint = "Due again 12 months later (49 CFR 396.17). Completing an \"Annual DOT inspection\" work order updates this for you." },
                new FormField { Key = "odo", Label = "Odometer", Kind = FieldKind.Number, Value = isNew ? null : v.Odometer },
                new FormField { Key = "driver", Label = "Assigned driver", Value = v.AssignedDriver },
                new FormField { Key = "location", Label = "Location / terminal", Value = v.Location },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = Vehicle.ServiceStatuses, Value = v.ServiceStatus },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = v.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add vehicle" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Vehicles.Delete(v);
            Toaster.Show($"Unit {v.UnitNumber} removed.");
            return null;
        }

        var unit = dialog.Text("unit");
        if (DataStore.Vehicles.Items.Any(x => !ReferenceEquals(x, v) && string.Equals(x.UnitNumber, unit, StringComparison.OrdinalIgnoreCase)))
        {
            Toaster.Show($"Unit {unit} is already in the fleet. Use a different unit number.");
            return null;
        }
        v.UnitNumber = unit;
        v.Type = dialog.Text("type");
        v.Year = dialog.Text("year");
        v.Make = dialog.Text("make");
        v.Model = dialog.Text("model");
        v.Vin = dialog.Text("vin").ToUpperInvariant();
        v.Plate = dialog.Text("plate").ToUpperInvariant();
        v.PlateState = dialog.Text("pstate");
        v.RegistrationExpires = dialog.Date("reg");
        v.LastAnnualInspection = dialog.Date("annual");
        v.Odometer = dialog.Number("odo") ?? v.Odometer;
        v.AssignedDriver = dialog.Text("driver");
        v.Location = dialog.Text("location");
        v.ServiceStatus = dialog.Text("status");
        v.Notes = dialog.Text("notes");

        if (isNew) DataStore.Vehicles.Add(v); else DataStore.Vehicles.Update(v);
        Toaster.Show($"Unit {v.UnitNumber} saved.");
        return v;
    }

    private static Vehicle? FindVehicle(string unit) =>
        DataStore.Vehicles.Items.FirstOrDefault(v => string.Equals(v.UnitNumber, unit.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string UnitHint() =>
        DataStore.Vehicles.Items.Count == 0 ? "Add vehicles in Vehicle Management to keep unit numbers consistent."
        : "Fleet units: " + string.Join(", ", DataStore.Vehicles.Items.Where(v => v.IsActive).Select(v => v.UnitNumber).OrderBy(u => u).Take(15));

    // ------------------------------------------------------------------ DVIR

    public static Dvir? EditDvir(Dvir? existing)
    {
        var r = existing ?? new Dvir();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New DVIR" : $"Edit {r.Number}",
            isNew ? "Driver vehicle inspection report. Defects must be repaired (or found not to need repair) and certified before the vehicle runs again."
                  : $"{r.TripType} · {r.DateText}",
            [
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)r.Date },
                new FormField { Key = "trip", Label = "Inspection", Kind = FieldKind.Choice, Options = Dvir.TripTypes, Value = r.TripType },
                new FormField { Key = "driver", Label = "Driver", Required = true, Value = r.Driver },
                new FormField { Key = "unit", Label = "Unit number", Required = true, Value = r.UnitNumber, Hint = UnitHint() },
                new FormField { Key = "odo", Label = "Odometer", Kind = FieldKind.Number, Value = isNew ? null : r.Odometer },
                new FormField { Key = "found", Label = "Result", Kind = FieldKind.Choice, Options = Dvir.DefectChoices, Value = r.DefectsFound ? Dvir.DefectChoices[1] : Dvir.DefectChoices[0] },
                new FormField { Key = "defects", Label = "Defects (brakes, lights, tires, coupling, steering…)", Kind = FieldKind.MultiLine, Value = r.Defects },
                new FormField { Key = "safe", Label = "Safe to operate?", Kind = FieldKind.Choice, Options = Dvir.SafeChoices, Value = r.SafeToOperate },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Submit DVIR" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Dvirs.Delete(r);
            Toaster.Show($"{r.Number} deleted.");
            return null;
        }

        r.Date = dialog.Date("date") ?? DateTime.Today;
        r.TripType = dialog.Text("trip");
        r.Driver = dialog.Text("driver");
        r.UnitNumber = dialog.Text("unit");
        r.Odometer = dialog.Number("odo") ?? 0;
        bool found = dialog.Text("found") != FleetStatus.NoDefects;
        if (found && !r.DefectsFound) { r.RepairedOn = null; r.RepairedBy = ""; }
        r.DefectsFound = found;
        r.Defects = dialog.Text("defects");
        r.SafeToOperate = dialog.Text("safe");

        if (isNew) DataStore.Dvirs.Add(r); else DataStore.Dvirs.Update(r);

        // Keep the vehicle's odometer current, and take it out of service when the driver says it isn't safe.
        if (FindVehicle(r.UnitNumber) is { } vehicle)
        {
            bool changed = false;
            if (r.Odometer > vehicle.Odometer) { vehicle.Odometer = r.Odometer; changed = true; }
            if (r.DefectsOpen && r.SafeToOperate != "Yes" && vehicle.ServiceStatus == FleetStatus.InService)
            {
                vehicle.ServiceStatus = FleetStatus.OutOfService;
                changed = true;
                Toaster.Show($"{r.Number} saved. Unit {vehicle.UnitNumber} is marked out of service until the defects are repaired.");
            }
            if (changed) DataStore.Vehicles.Update(vehicle);
        }
        if (!(r.DefectsOpen && r.SafeToOperate != "Yes")) Toaster.Show(isNew ? $"{r.Number} submitted." : $"{r.Number} saved.");
        return r;
    }

    /// <summary>Mechanic certifies the defects were repaired (49 CFR 396.11).</summary>
    public static void CertifyDvirRepair(Dvir r)
    {
        var dialog = new FormDialog(
            $"Certify repair: {r.Number}",
            $"Unit {r.UnitNumber}: {r.DefectsText}",
            [
                new FormField { Key = "by", Label = "Repaired / certified by", Required = true },
                new FormField { Key = "date", Label = "Repair date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)DateTime.Today },
                new FormField { Key = "notes", Label = "What was done", Kind = FieldKind.MultiLine, Hint = "Or why repair wasn't needed for safe operation." },
            ],
            allowDelete: false,
            saveText: "Certify repair");
        if (dialog.ShowDialog() != true) return;

        r.RepairedBy = dialog.Text("by");
        r.RepairedOn = dialog.Date("date") ?? DateTime.Today;
        r.RepairNotes = dialog.Text("notes");
        DataStore.Dvirs.Update(r);

        if (FindVehicle(r.UnitNumber) is { ServiceStatus: FleetStatus.OutOfService } vehicle &&
            !DataStore.Dvirs.Items.Any(x => x.DefectsOpen && string.Equals(x.UnitNumber, vehicle.UnitNumber, StringComparison.OrdinalIgnoreCase)))
        {
            vehicle.ServiceStatus = FleetStatus.InService;
            DataStore.Vehicles.Update(vehicle);
            Toaster.Show($"{r.Number} repair certified. Unit {vehicle.UnitNumber} is back in service.");
            return;
        }
        Toaster.Show($"{r.Number} repair certified.");
    }

    // ------------------------------------------------------------------ Maintenance

    public static MaintenanceOrder? EditMaintenance(MaintenanceOrder? existing)
    {
        var w = existing ?? new MaintenanceOrder();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Schedule service" : $"Edit {w.Number}",
            isNew ? "Preventive maintenance, repairs and the annual DOT inspection. Repeating services schedule the next one when you mark them complete."
                  : $"Unit {w.UnitNumber} · {w.Service}",
            [
                new FormField { Key = "unit", Label = "Unit number", Required = true, Value = w.UnitNumber, Hint = UnitHint() },
                new FormField { Key = "service", Label = "Service", Kind = FieldKind.Choice, Options = MaintenanceOrder.Services, Value = w.Service },
                new FormField { Key = "desc", Label = "Details", Kind = FieldKind.MultiLine, Value = w.Description },
                new FormField { Key = "due", Label = "Due date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)w.DueDate },
                new FormField { Key = "dueodo", Label = "Or due at odometer (optional)", Kind = FieldKind.Number, Value = w.DueOdometer == 0 ? null : w.DueOdometer },
                new FormField { Key = "repeat", Label = "Repeats", Kind = FieldKind.Choice, Options = MaintenanceOrder.Repeats, Value = w.Repeat },
                new FormField { Key = "vendor", Label = "Shop / vendor", Value = w.Vendor },
                new FormField { Key = "cost", Label = "Cost", Kind = FieldKind.Number, Value = w.Cost == 0 ? null : w.Cost },
                new FormField { Key = "done", Label = "Completed on", Kind = FieldKind.Date, Value = w.CompletedOn, Hint = "Leave blank until the work is done (or use \"Mark complete\" on the list)." },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = w.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Schedule" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Maintenance.Delete(w);
            Toaster.Show($"{w.Number} deleted.");
            return null;
        }

        bool wasComplete = w.IsComplete;
        w.UnitNumber = dialog.Text("unit");
        w.Service = dialog.Text("service");
        w.Description = dialog.Text("desc");
        w.DueDate = dialog.Date("due") ?? DateTime.Today;
        w.DueOdometer = dialog.Number("dueodo") ?? 0;
        w.Repeat = dialog.Text("repeat");
        w.Vendor = dialog.Text("vendor");
        w.Cost = dialog.Number("cost") ?? 0;
        w.CompletedOn = dialog.Date("done");
        w.Notes = dialog.Text("notes");

        if (isNew) DataStore.Maintenance.Add(w); else DataStore.Maintenance.Update(w);
        if (!wasComplete && w.IsComplete) AfterMaintenanceCompleted(w);
        else Toaster.Show(isNew ? $"{w.Number} scheduled." : $"{w.Number} saved.");
        return w;
    }

    public static void CompleteMaintenance(MaintenanceOrder w)
    {
        if (w.IsComplete) return;
        w.CompletedOn = DateTime.Today;
        DataStore.Maintenance.Update(w);
        AfterMaintenanceCompleted(w);
    }

    /// <summary>Schedules the next repeat and records an annual inspection on the vehicle.</summary>
    private static void AfterMaintenanceCompleted(MaintenanceOrder w)
    {
        var notes = new List<string> { $"{w.Number} complete." };
        if (w.Service == MaintenanceOrder.AnnualInspection && FindVehicle(w.UnitNumber) is { } vehicle)
        {
            vehicle.LastAnnualInspection = w.CompletedOn;
            DataStore.Vehicles.Update(vehicle);
            notes.Add($"Unit {vehicle.UnitNumber}'s annual inspection is updated.");
        }
        if (w.RepeatMonths > 0)
        {
            var next = new MaintenanceOrder
            {
                UnitNumber = w.UnitNumber,
                Service = w.Service,
                Description = w.Description,
                Vendor = w.Vendor,
                Repeat = w.Repeat,
                DueDate = (w.CompletedOn ?? DateTime.Today).AddMonths(w.RepeatMonths),
            };
            DataStore.Maintenance.Add(next);
            notes.Add($"Next one ({next.Number}) is due {next.DueText}.");
        }
        Toaster.Show(string.Join(" ", notes));
    }

    // ------------------------------------------------------------------ Hours of Service

    public static HosLog? EditHos(HosLog? existing)
    {
        var h = existing ?? new HosLog();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Review a driver's log" : $"Edit {h.Number}",
            "Record a reviewed day from the driver's ELD or paper log. Over 11 hours driving or a 14-hour on-duty window is flagged automatically (property-carrying rules).",
            [
                new FormField { Key = "date", Label = "Log date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)h.Date },
                new FormField { Key = "driver", Label = "Driver", Required = true, Value = h.Driver },
                new FormField { Key = "driving", Label = "Driving hours", Kind = FieldKind.Number, Required = true, Value = isNew ? null : h.DrivingHours },
                new FormField { Key = "onduty", Label = "On-duty window (hours)", Kind = FieldKind.Number, Value = isNew ? null : h.OnDutyHours },
                new FormField { Key = "violation", Label = "Violation found", Kind = FieldKind.Choice, Options = HosLog.Violations, Value = h.Violation },
                new FormField { Key = "by", Label = "Reviewed by", Value = h.ReviewedBy },
                new FormField { Key = "notes", Label = "Notes / coaching", Kind = FieldKind.MultiLine, Value = h.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Save review" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Hos.Delete(h);
            Toaster.Show($"{h.Number} deleted.");
            return null;
        }

        h.Date = dialog.Date("date") ?? DateTime.Today;
        h.Driver = dialog.Text("driver");
        h.DrivingHours = dialog.Number("driving") ?? 0;
        h.OnDutyHours = dialog.Number("onduty") ?? 0;
        var chosen = dialog.Text("violation");
        h.Violation = HosLog.DetectViolation(h.DrivingHours, h.OnDutyHours, chosen);
        h.ReviewedBy = dialog.Text("by");
        h.Notes = dialog.Text("notes");

        if (isNew) DataStore.Hos.Add(h); else DataStore.Hos.Update(h);
        Toaster.Show(h.HasViolation && chosen == HosLog.None
            ? $"{h.Number} saved and flagged: {h.Violation} violation."
            : $"{h.Number} saved.");
        return h;
    }

    // ------------------------------------------------------------------ Accident Register

    public static FleetAccident? EditFleetAccident(FleetAccident? existing)
    {
        var a = existing ?? new FleetAccident();
        bool isNew = existing is null;
        string? currentFile = a.HasFile ? Path.Combine(DataStore.Folder, a.StoredFile) : null;

        var dialog = new FormDialog(
            isNew ? "Log a vehicle accident" : $"Edit {a.Number}",
            isNew ? "The DOT accident register (49 CFR 390.15). Recordable: a fatality, an injury treated away from the scene, or a vehicle towed away."
                  : $"{a.DateText} · {a.RecordableText}",
            [
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)a.Date },
                new FormField { Key = "time", Label = "Time", Value = a.Time },
                new FormField { Key = "driver", Label = "Driver", Required = true, Value = a.Driver },
                new FormField { Key = "unit", Label = "Unit number", Value = a.UnitNumber, Hint = UnitHint() },
                new FormField { Key = "where", Label = "Where (street / highway)", Value = a.Location },
                new FormField { Key = "city", Label = "City and state", Value = a.CityState },
                new FormField { Key = "desc", Label = "What happened?", Kind = FieldKind.MultiLine, Required = true, Value = a.Description },
                new FormField { Key = "injuries", Label = "People injured (treated away from the scene)", Kind = FieldKind.Number, Value = isNew ? null : (decimal)a.Injuries },
                new FormField { Key = "fatalities", Label = "Fatalities", Kind = FieldKind.Number, Value = isNew ? null : (decimal)a.Fatalities },
                new FormField { Key = "tow", Label = "Any vehicle towed away?", Kind = FieldKind.Choice, Options = FleetAccident.YesNo, Value = a.TowAway },
                new FormField { Key = "hazmat", Label = "Hazardous materials released?", Kind = FieldKind.Choice, Options = FleetAccident.YesNo, Value = a.Hazmat },
                new FormField { Key = "citation", Label = "Driver cited?", Kind = FieldKind.Choice, Options = FleetAccident.YesNo, Value = a.Citation },
                new FormField { Key = "police", Label = "Police report number", Value = a.PoliceReport },
                new FormField { Key = "prevent", Label = "Preventability", Kind = FieldKind.Choice, Options = FleetAccident.Preventabilities, Value = a.Preventability },
                new FormField { Key = "file", Label = "Dashcam / CCTV video (optional)", Kind = FieldKind.File, Value = currentFile,
                                Hint = "Kept with the record. Automatic AI fault review (Crash Review AI) is coming later." },
                new FormField { Key = "notes", Label = "Notes / review", Kind = FieldKind.MultiLine, Value = a.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Log accident" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.FleetAccidents.Delete(a);
            TryDeleteFolder(Path.Combine(DataStore.Folder, "accidents", a.Id.ToString("N")));
            Toaster.Show($"{a.Number} deleted.");
            return null;
        }

        if (!TryStoreChosenFile("accidents", a.Id, dialog.Text("file"), currentFile, f => a.StoredFile = f)) return null;
        a.Date = dialog.Date("date") ?? DateTime.Today;
        a.Time = dialog.Text("time");
        a.Driver = dialog.Text("driver");
        a.UnitNumber = dialog.Text("unit");
        a.Location = dialog.Text("where");
        a.CityState = dialog.Text("city");
        a.Description = dialog.Text("desc");
        a.Injuries = (int)Math.Round(dialog.Number("injuries") ?? 0);
        a.Fatalities = (int)Math.Round(dialog.Number("fatalities") ?? 0);
        a.TowAway = dialog.Text("tow");
        a.Hazmat = dialog.Text("hazmat");
        a.Citation = dialog.Text("citation");
        a.PoliceReport = dialog.Text("police");
        a.Preventability = dialog.Text("prevent");
        a.Notes = dialog.Text("notes");

        if (isNew) DataStore.FleetAccidents.Add(a); else DataStore.FleetAccidents.Update(a);
        Toaster.Show(isNew ? $"{a.Number} logged ({a.RecordableText.ToLowerInvariant()})." : $"{a.Number} saved.");
        return a;
    }
}
