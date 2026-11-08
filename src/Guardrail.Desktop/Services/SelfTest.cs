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
