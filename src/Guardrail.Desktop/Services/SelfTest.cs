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
