using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using Guardrail.Data;
using Guardrail.Models;
using Guardrail.Views;

namespace Guardrail.Services;

/// <summary>
/// Headless checks run by CI: <c>Guardrail.exe --self-test result.txt</c>.
/// Opens every page in both themes, builds the add/edit dialog, and round-trips the
/// JSON store in a temp folder (never the user's data). Exit code = number of failures.
/// </summary>
public static class SelfTest
{
    public static string? OutputFile { get; set; }
    public static bool IsRunning => OutputFile != null;

    private static readonly StringBuilder Report = new();
    private static int _failed;

    /// <summary>Called by App's unhandled-exception handler while the self-test runs.</summary>
    public static void RecordUnhandled(Exception ex)
    {
        _failed++;
        Report.AppendLine($"FAIL  Unhandled exception — {ex}");
    }

    private static void Check(string name, bool ok, string detail = "")
    {
        if (!ok) _failed++;
        Report.AppendLine($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  — " + detail : "")}");
    }

    private static async Task Step(string name, Action action)
    {
        try
        {
            action();
            // Let layout, data binding and rendering run so XAML errors surface here.
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Application.Current.MainWindow?.UpdateLayout();
            Check(name, true);
        }
        catch (Exception ex)
        {
            Check(name, false, ex.ToString());
        }
    }

    public static async Task<int> RunAsync(MainWindow window)
    {
        Report.AppendLine($"Guardrail EHS self-test {DateTime.Now:u}");

        foreach (var theme in new[] { AppTheme.Light, AppTheme.Dark })
        {
            await Step($"[{theme}] Apply theme", () => App.ApplyTheme(theme));
            foreach (var module in ModuleCatalog.Toolbar)
                await Step($"[{theme}] Page: {module.Title}", () => window.GoTo(module.Key));
            foreach (var section in ModuleCatalog.FleetSections)
                await Step($"[{theme}] Fleet Hub: {section.Title}", () => window.GoTo(section.Key));
            await Step($"[{theme}] Page: Settings", window.ShowSettings);
            await Step($"[{theme}] Actions filtered to Overdue", () => window.OpenModule("actions", new ActionsView("Overdue")));
        }

        // The shared add/edit dialog with every field type.
        await Step("Form dialog builds and shows", () =>
        {
            var dialog = new FormDialog("Self-test", "Every field type", new[]
            {
                new FormField { Key = "t", Label = "Text", Required = true, Value = "hello" },
                new FormField { Key = "m", Label = "Multi-line", Kind = FieldKind.MultiLine, Value = "a\nb" },
                new FormField { Key = "d", Label = "Date", Kind = FieldKind.Date, Value = (DateTime?)DateTime.Today },
                new FormField { Key = "c", Label = "Choice", Kind = FieldKind.Choice, Options = ["A", "B"], Value = "B" },
                new FormField { Key = "n", Label = "Number", Kind = FieldKind.Number, Value = 2500.5m },
                new FormField { Key = "f", Label = "File", Kind = FieldKind.File, Value = @"C:\example\policy.pdf" },
            }, allowDelete: true);
            dialog.Show();
            dialog.UpdateLayout();
            dialog.Close();
        });

        // The inspection checklist window.
        await Step("Checklist window builds and shows", () =>
        {
            var starter = InspectionTemplate.Starters()[0];
            var inspection = new Inspection
            {
                TemplateName = starter.Name,
                Location = "Self-test",
                Items = starter.Items.Select(t => new ChecklistItem { Text = t }).ToList(),
            };
            inspection.Items[0].Result = ChecklistItem.Fail;
            var window = new ChecklistWindow(inspection);
            window.Show();
            window.UpdateLayout();
            window.Close();
        });

        // JSON store round trip in a temp folder.
        var temp = Path.Combine(Path.GetTempPath(), "guardrail-selftest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new JsonStore<ActionItem>(temp, "actions");
            var a = new ActionItem { Title = "Fix guard rail", DueDate = DateTime.Today.AddDays(-1) };
            store.Add(a);
            Check("Store: first record numbered ACT-0001", a.Number == "ACT-0001", a.Number);
            Check("Store: past due date shows Overdue", a.DisplayStatus == Status.Overdue, a.DisplayStatus);
            store.Add(new ActionItem { Title = "Second" });
            var reloaded = new JsonStore<ActionItem>(temp, "actions");
            Check("Store: reload keeps both records", reloaded.Items.Count == 2, reloaded.Items.Count.ToString());
            Check("Store: reload keeps fields", reloaded.Items[0].Title == "Fix guard rail" && reloaded.Items[0].DueDate == a.DueDate);
            var first = reloaded.Items[0];
            first.Status = Status.Closed;
            reloaded.Update(first);
            reloaded.Delete(reloaded.Items[1]);
            var again = new JsonStore<ActionItem>(temp, "actions");
            Check("Store: update + delete persisted", again.Items.Count == 1 && again.Items[0].IsClosed);

            var t = new TrainingRecord { Employee = "Pat", Course = "Forklift", ExpiresOn = DateTime.Today.AddDays(10) };
            Check("Training: expiring in 10 days is Expiring soon", t.DisplayStatus == Status.ExpiringSoon, t.DisplayStatus);
            t.ExpiresOn = DateTime.Today.AddDays(-1);
            Check("Training: past expiry is Expired", t.DisplayStatus == Status.Expired, t.DisplayStatus);

            // Regulatory Compliance
            var yearly = new ComplianceItem { Title = "Stormwater permit", Frequency = "Yearly", DueDate = DateTime.Today.AddDays(-40) };
            Check("Compliance: past due is Overdue", yearly.DisplayStatus == Status.Overdue, yearly.DisplayStatus);
            yearly.MarkDone(DateTime.Today);
            Check("Compliance: yearly rolls forward past today", !yearly.IsCompleted && yearly.DueDate > DateTime.Today
                  && yearly.DueDate <= DateTime.Today.AddYears(1), yearly.DueDate.ToString("yyyy-MM-dd"));
            var monthly = new ComplianceItem { Frequency = "Monthly", DueDate = DateTime.Today.AddMonths(-3) };
            monthly.MarkDone(DateTime.Today);
            Check("Compliance: missed months skip to the next one ahead", monthly.DueDate > DateTime.Today
                  && monthly.DueDate <= DateTime.Today.AddMonths(1), monthly.DueDate.ToString("yyyy-MM-dd"));
            var once = new ComplianceItem { Frequency = "One-time", DueDate = DateTime.Today.AddDays(10) };
            Check("Compliance: due in 10 days is Due soon", once.DisplayStatus == Status.DueSoon, once.DisplayStatus);
            once.MarkDone(DateTime.Today);
            Check("Compliance: one-time item completes", once.IsCompleted && once.DisplayStatus == Status.Completed);

            // Claims Register
            var claim = new Claim { Claimant = "Pat", Reserve = 5000m, Paid = 1250.50m };
            Check("Claims: incurred = paid + open reserve", claim.Incurred == 6250.50m, claim.Incurred.ToString());
            claim.Status = Status.Closed;
            Check("Claims: closed claim drops its reserve", claim.Incurred == 1250.50m, claim.Incurred.ToString());
            var claims = new JsonStore<Claim>(temp, "claims");
            claims.Add(new Claim { Claimant = "Lee", Reserve = 1234.56m });
            var claimsAgain = new JsonStore<Claim>(temp, "claims");
            Check("Claims: money survives save/load", claimsAgain.Items[0].Reserve == 1234.56m && claimsAgain.Items[0].Number == "CLM-0001");

            // Library: the file is copied in, and replacing it leaves one file
            var srcA = Path.Combine(temp, "policy-v1.txt");
            var srcB = Path.Combine(temp, "policy-v2.txt");
            File.WriteAllText(srcA, "v1");
            File.WriteAllText(srcB, "v2");
            var docId = Guid.NewGuid();
            var stored1 = Editors.StoreLibraryFile(docId, srcA);
            var stored2 = Editors.StoreLibraryFile(docId, srcB);
            var docFolder = Path.Combine(DataStore.LibraryFolder, docId.ToString("N"));
            Check("Library: file copied into the data folder", File.Exists(Path.Combine(DataStore.Folder, stored2))
                  && File.ReadAllText(Path.Combine(DataStore.Folder, stored2)) == "v2", stored2);
            Check("Library: replacing the file keeps only the new one", Directory.GetFiles(docFolder).Length == 1 && stored1 != stored2);
            try { Directory.Delete(docFolder, recursive: true); } catch { /* ignore */ }
            var doc = new LibraryDocument { ReviewDue = DateTime.Today.AddDays(-1) };
            Check("Library: past review date is Review overdue", doc.DisplayStatus == Status.ReviewOverdue, doc.DisplayStatus);

            // Monthly Data
            Check("Monthly: any day maps to the 1st", MonthlyEntry.FirstOfMonth(new DateTime(2026, 3, 17)) == new DateTime(2026, 3, 1));
            var months = new JsonStore<MonthlyEntry>(temp, "monthly");
            months.Add(new MonthlyEntry { Month = new DateTime(2026, 3, 1), Location = "Plant", HoursWorked = 17325.5m, MilesDriven = 4200m });
            var monthsAgain = new JsonStore<MonthlyEntry>(temp, "monthly");
            Check("Monthly: hours and miles survive save/load", monthsAgain.Items[0].HoursWorked == 17325.5m && monthsAgain.Items[0].MilesDriven == 4200m);

            // Behavioral Observations
            var card = new BbsObservation { SafeCount = 9, AtRiskCount = 1 };
            Check("BBS: 9 safe / 1 at-risk is 90% safe", card.PercentSafeText == "90%", card.PercentSafeText);
            Check("BBS: at-risk card can become an action", card.CanCreateAction && card.DisplayStatus == Status.AtRiskFound);
            card.LinkedAction = "ACT-0001";
            Check("BBS: linked card can't create a second action", !card.CanCreateAction && card.HasLinkedAction);
            Check("BBS: all-safe card", new BbsObservation { SafeCount = 5 }.DisplayStatus == Status.AllSafe);

            // Work Permits
            var permit = new WorkPermit { ValidFrom = DateTime.Today.AddDays(-2), ValidUntil = DateTime.Today.AddDays(-1) };
            Check("Permits: active past its end date shows Expired", permit.DisplayStatus == Status.Expired, permit.DisplayStatus);
            permit.Status = Status.Closed;
            Check("Permits: closed permit isn't Expired", permit.DisplayStatus == Status.Closed, permit.DisplayStatus);
            Check("Permits: valid today", new WorkPermit().DisplayStatus == Status.Active);

            // Lockout / Tagout
            Check("LOTO: never inspected", new LotoProcedure().DisplayStatus == Status.NotInspected);
            Check("LOTO: inspected 400 days ago is Overdue", new LotoProcedure { LastInspection = DateTime.Today.AddDays(-400) }.DisplayStatus == Status.Overdue);
            Check("LOTO: inspected 350 days ago is Due soon", new LotoProcedure { LastInspection = DateTime.Today.AddDays(-350) }.DisplayStatus == Status.DueSoon);
            Check("LOTO: inspected today is Current", new LotoProcedure { LastInspection = DateTime.Today }.DisplayStatus == Status.Current);

            // Safety Data Sheets (file storage shared with Library and LOTO)
            var sdsId = Guid.NewGuid();
            var sdsFile = Editors.StoreFile("sds", sdsId, srcA);
            var sds = new SdsEntry { Product = "Acetone", StoredFile = sdsFile };
            Check("SDS: file stored under data\\sds", sdsFile.StartsWith("sds") && File.Exists(Path.Combine(DataStore.Folder, sdsFile)), sdsFile);
            Check("SDS: with a file is On file", sds.DisplayStatus == Status.OnFile);
            Check("SDS: without a file is Missing SDS", new SdsEntry().DisplayStatus == Status.MissingSds);
            try { Directory.Delete(Path.Combine(DataStore.Folder, "sds", sdsId.ToString("N")), recursive: true); } catch { /* ignore */ }

            // Incidents and OSHA rates
            var cut = new Incident { Type = Incident.InjuryIllness, Classification = Incident.MedicalTreatment };
            var sprain = new Incident { Type = Incident.InjuryIllness, Classification = Incident.DaysAwayCase, DaysAway = 5 };
            var firstAid = new Incident { Type = Incident.InjuryIllness, Classification = Incident.FirstAid };
            var spill = new Incident { Type = "Environmental", Classification = Incident.DaysAwayCase };
            Check("Incidents: medical treatment is recordable", cut.IsRecordable && !cut.IsDart);
            Check("Incidents: days away is recordable, DART and lost time", sprain.IsRecordable && sprain.IsDart && sprain.IsLostTime);
            Check("Incidents: first aid only is not recordable", !firstAid.IsRecordable);
            Check("Incidents: only injury / illness cases are recordable", !spill.IsRecordable);
            var metrics = new SafetyMetrics { Incidents = [cut, sprain, firstAid, spill], Hours = 200_000m, Miles = 500_000m };
            Check("Rates: TRIR = 2 cases × 200,000 ÷ 200,000 hours", SafetyMetrics.Format(metrics.Trir) == "2.00", SafetyMetrics.Format(metrics.Trir));
            Check("Rates: DART and LTIR", SafetyMetrics.Format(metrics.Dart) == "1.00" && SafetyMetrics.Format(metrics.Ltir) == "1.00");
            Check("Rates: lost days rate", SafetyMetrics.Format(metrics.LostDaysRate) == "5.00", SafetyMetrics.Format(metrics.LostDaysRate));
            Check("Rates: no hours shows a dash", SafetyMetrics.Format(new SafetyMetrics { Incidents = [cut] }.Trir) == "—");
            var vehicle = new SafetyMetrics { Incidents = [new Incident { Type = Incident.MotorVehicle }], Miles = 500_000m, ExtraVehicleAccidents = 1 };
            Check("Rates: accident frequency per million miles", SafetyMetrics.Format(vehicle.AccidentFrequencyRate) == "4.00", SafetyMetrics.Format(vehicle.AccidentFrequencyRate));

            // Risk Assessment
            Check("Risk: 2 × 2 is Low", RiskAssessment.LevelFor(4) == Status.Low);
            Check("Risk: 3 × 3 is Medium", RiskAssessment.LevelFor(9) == Status.Medium);
            Check("Risk: 3 × 4 is High", RiskAssessment.LevelFor(12) == Status.High);
            Check("Risk: 4 × 4 is Extreme", RiskAssessment.LevelFor(16) == Status.Extreme);
            Check("Risk: rating labels parse", RiskAssessment.ParseRating("4 – Likely") == 4 && RiskAssessment.ParseRating("") == 3);

            // Audits & Inspections
            var audit = new Inspection
            {
                IsComplete = true,
                Items =
                [
                    new ChecklistItem { Result = ChecklistItem.Pass }, new ChecklistItem { Result = ChecklistItem.Pass },
                    new ChecklistItem { Result = ChecklistItem.Pass }, new ChecklistItem { Result = ChecklistItem.Fail },
                    new ChecklistItem { Result = ChecklistItem.NotApplicable },
                ],
            };
            Check("Audits: score ignores N/A (3 of 4 = 75%)", audit.ScoreText == "75%", audit.ScoreText);
            Check("Audits: failed item is a finding that can become an action", audit.DisplayStatus == Status.FindingsOpen && audit.CanCreateActions);
            Check("Audits: starter checklists", InspectionTemplate.Starters().Count == 3 && InspectionTemplate.Starters().All(t => t.Items.Count > 0));

            // Management of Change
            var change = new ChangeRequest();
            Check("MOC: requested → under review", change.NextStage == Status.UnderReview);
            change.Stage = Status.Implemented;
            Check("MOC: implemented → closed", change.NextStage == Status.Closed);
            var temp1 = new ChangeRequest { Duration = "Temporary", TemporaryUntil = DateTime.Today.AddDays(-1) };
            Check("MOC: temporary change past its date is flagged", temp1.DisplayStatus == Status.TemporaryExpired);

            // Fleet Hub
            var driver = new Driver
            {
                ApplicationStatus = FleetStatus.Accepted, RoadTest = "Yes",
                LicenseExpires = DateTime.Today.AddYears(2), MedicalCardExpires = DateTime.Today.AddYears(1), LastMvrReview = DateTime.Today.AddMonths(-2),
            };
            Check("Fleet: complete driver file is Qualified", driver.DisplayStatus == FleetStatus.Qualified, driver.DisplayStatus);
            driver.MedicalCardExpires = DateTime.Today.AddDays(10);
            Check("Fleet: medical card in 10 days is Expiring soon", driver.DisplayStatus == Status.ExpiringSoon, driver.DisplayStatus);
            driver.LastMvrReview = DateTime.Today.AddMonths(-13);
            Check("Fleet: MVR review over a year old is Expired", driver.DisplayStatus == Status.Expired && driver.Problems.Count == 1, string.Join("; ", driver.Problems));
            Check("Fleet: applicant shows application status", new Driver().DisplayStatus == FleetStatus.Applied);
            Check("Fleet: missing dates make the file Incomplete", new Driver { ApplicationStatus = FleetStatus.Accepted }.DisplayStatus == FleetStatus.Incomplete);

            var truck = new Vehicle { LastAnnualInspection = DateTime.Today.AddMonths(-11).AddDays(-20), RegistrationExpires = DateTime.Today.AddYears(1) };
            Check("Fleet: annual inspection due in about 10 days is Due soon", truck.DisplayStatus == Status.DueSoon, truck.DisplayStatus);
            truck.LastAnnualInspection = DateTime.Today.AddYears(-2);
            Check("Fleet: annual inspection overdue", truck.DisplayStatus == FleetStatus.InspectionOverdue, truck.DisplayStatus);
            truck.RegistrationExpires = DateTime.Today.AddDays(-1);
            Check("Fleet: expired registration comes first", truck.DisplayStatus == FleetStatus.RegistrationExpired, truck.DisplayStatus);

            var dvir = new Dvir { DefectsFound = true };
            Check("Fleet: DVIR with defects is open until repaired", dvir.DefectsOpen && dvir.DisplayStatus == FleetStatus.DefectsOpen);
            dvir.RepairedOn = DateTime.Today;
            Check("Fleet: certified DVIR is Repaired", !dvir.DefectsOpen && dvir.DisplayStatus == FleetStatus.Repaired);

            Check("Fleet: 11.5 hours driving is an 11-hour violation", HosLog.DetectViolation(11.5m, 13m, HosLog.None) == "11-hour driving");
            Check("Fleet: 14.5-hour window is a 14-hour violation", HosLog.DetectViolation(10m, 14.5m, HosLog.None) == "14-hour window");
            Check("Fleet: 10 hours driving in a 12-hour window is compliant", HosLog.DetectViolation(10m, 12m, HosLog.None) == HosLog.None);

            Check("Fleet: tow-away makes an accident DOT recordable", new FleetAccident { TowAway = "Yes" }.IsDotRecordable);
            Check("Fleet: property damage only is not DOT recordable", !new FleetAccident().IsDotRecordable);
            Check("Fleet: quarterly service repeats every 3 months", new MaintenanceOrder { Repeat = "Every 3 months" }.RepeatMonths == 3);
            Check("Fleet: overdue work order", new MaintenanceOrder { DueDate = DateTime.Today.AddDays(-1) }.DisplayStatus == Status.Overdue);

            var register = new SafetyMetrics { Incidents = [new Incident { Type = Incident.MotorVehicle }], Miles = 1_000_000m, UseFleetRegister = true, FleetAccidents = 3 };
            Check("Fleet: accident register drives the accident frequency rate", SafetyMetrics.Format(register.AccidentFrequencyRate) == "3.00", SafetyMetrics.Format(register.AccidentFrequencyRate));

            var audit2 = FleetInsights.DotAudit();
            Check("Fleet: DOT mock audit runs every check", audit2.Count == 10, audit2.Count.ToString());
            Check("Fleet: overview alerts build", FleetInsights.Alerts() is not null);

            // Every report builds
            foreach (var report in ReportCatalog.All)
            {
                try
                {
                    var table = report.Build(DateTime.Today.Year);
                    Check($"Report builds: {report.Title}", table.Headers.Length > 0 && table.Rows.All(r => r.Length <= table.Headers.Length));
                }
                catch (Exception ex) { Check($"Report builds: {report.Title}", false, ex.Message); }
            }
        }
        catch (Exception ex)
        {
            Check("Store round trip", false, ex.ToString());
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }

        await Step("Back to Dashboard", () => window.GoTo(ModuleCatalog.Dashboard.Key));

        Report.AppendLine(_failed == 0 ? "ALL PASSED" : $"{_failed} FAILED");
        try { File.WriteAllText(OutputFile!, Report.ToString()); } catch { /* ignore */ }
        return _failed;
    }
}
