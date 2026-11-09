using System.Diagnostics;
using System.IO;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Add / edit / delete dialogs for each working module.</summary>
public static class Editors
{
    /// <summary>Opens the action editor. Pass <paramref name="existing"/> to edit, or a <paramref name="draft"/> to pre-fill a new one.</summary>
    /// <returns>The saved action, or null if cancelled or deleted.</returns>
    public static ActionItem? EditAction(ActionItem? existing, ActionItem? draft = null)
    {
        var a = existing ?? draft ?? new ActionItem();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New action" : $"Edit {a.Number}",
            isNew ? "A corrective or preventive action with an owner and a due date." : $"Created {a.CreatedAt:MMM d, yyyy}" + (a.Source.Length > 0 ? $" · from {a.Source}" : ""),
            [
                new FormField { Key = "title", Label = "What needs to be done", Required = true, Value = a.Title },
                new FormField { Key = "desc", Label = "Details", Kind = FieldKind.MultiLine, Value = a.Description },
                new FormField { Key = "owner", Label = "Owner", Value = a.Owner, Hint = "Who is responsible for getting this done." },
                new FormField { Key = "location", Label = "Location", Value = a.Location },
                new FormField { Key = "due", Label = "Due date", Kind = FieldKind.Date, Value = a.DueDate },
                new FormField { Key = "priority", Label = "Priority", Kind = FieldKind.Choice, Options = ActionItem.Priorities, Value = a.Priority },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = ActionItem.Statuses, Value = a.Status },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Create action" : "Save changes");

        if (dialog.ShowDialog() != true) return null;

        if (dialog.DeleteRequested)
        {
            DataStore.Actions.Delete(a);
            Toaster.Show($"{a.Number} deleted.");
            return null;
        }

        string oldStatus = a.Status;
        a.Title = dialog.Text("title");
        a.Description = dialog.Text("desc");
        a.Owner = dialog.Text("owner");
        a.Location = dialog.Text("location");
        a.DueDate = dialog.Date("due");
        a.Priority = dialog.Text("priority");
        a.Status = dialog.Text("status");
        if (a.Status == Status.Closed && oldStatus != Status.Closed) a.ClosedAt = DateTime.Now;
        if (a.Status != Status.Closed) a.ClosedAt = null;

        if (isNew) DataStore.Actions.Add(a); else DataStore.Actions.Update(a);
        Toaster.Show(isNew ? $"{a.Number} created." : $"{a.Number} saved.");
        return a;
    }

    public static Observation? EditObservation(Observation? existing)
    {
        var o = existing ?? new Observation();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New observation" : $"Edit {o.Number}",
            isNew ? "Report a hazard or recognize a good catch. Takes 30 seconds." :
                    $"Reported {o.CreatedAt:MMM d, yyyy}" + (o.LinkedAction.Length > 0 ? $" · action {o.LinkedAction}" : ""),
            [
                new FormField { Key = "type", Label = "Type", Kind = FieldKind.Choice, Options = Observation.Types, Value = o.Type },
                new FormField { Key = "desc", Label = "What did you see?", Kind = FieldKind.MultiLine, Required = true, Value = o.Description },
                new FormField { Key = "date", Label = "Date observed", Kind = FieldKind.Date, Required = true, Value = (DateTime?)o.Date },
                new FormField { Key = "location", Label = "Location", Value = o.Location },
                new FormField { Key = "by", Label = "Reported by", Value = o.ReportedBy, Hint = "Leave blank to report anonymously." },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = Observation.Statuses, Value = o.Status },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Submit observation" : "Save changes");

        if (dialog.ShowDialog() != true) return null;

        if (dialog.DeleteRequested)
        {
            DataStore.Observations.Delete(o);
            Toaster.Show($"{o.Number} deleted.");
            return null;
        }

        o.Type = dialog.Text("type");
        o.Description = dialog.Text("desc");
        o.Date = dialog.Date("date") ?? DateTime.Today;
        o.Location = dialog.Text("location");
        o.ReportedBy = dialog.Text("by");
        o.Status = dialog.Text("status");

        if (isNew) DataStore.Observations.Add(o); else DataStore.Observations.Update(o);
        Toaster.Show(isNew ? $"{o.Number} submitted." : $"{o.Number} saved.");
        return o;
    }

    /// <summary>Turns an observation into a pre-filled action and links the two.</summary>
    public static void CreateActionFrom(Observation o)
    {
        string summary = o.Description.ReplaceLineEndings(" ");
        if (summary.Length > 80) summary = summary[..77].TrimEnd() + "…";

        var draft = new ActionItem
        {
            Title = (o.IsGoodCatch ? "Follow up: " : "Fix: ") + summary,
            Description = $"From {o.Number} ({o.Type}, {o.DateText}):\n{o.Description}",
            Location = o.Location,
            DueDate = DateTime.Today.AddDays(7),
            Source = o.Number,
        };

        var saved = EditAction(null, draft);
        if (saved is null) return;

        o.LinkedAction = saved.Number;
        o.Status = Status.ActionCreated;
        DataStore.Observations.Update(o);
    }

    public static TrainingRecord? EditTraining(TrainingRecord? existing)
    {
        var t = existing ?? new TrainingRecord();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Record training" : $"Edit {t.Number}",
            isNew ? "Log a completed course. Guardrail warns you 30 days before it expires." : $"Added {t.CreatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "employee", Label = "Employee", Required = true, Value = t.Employee },
                new FormField { Key = "course", Label = "Course", Required = true, Value = t.Course, Hint = "For example: Forklift operator, First aid / CPR, HAZWOPER 8-hour refresher." },
                new FormField { Key = "completed", Label = "Completed on", Kind = FieldKind.Date, Required = true, Value = (DateTime?)t.CompletedOn },
                new FormField { Key = "expires", Label = "Expires on", Kind = FieldKind.Date, Value = t.ExpiresOn, Hint = "Leave blank if this training doesn't expire." },
                new FormField { Key = "trainer", Label = "Trainer / provider", Value = t.Trainer },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = t.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Save record" : "Save changes");

        if (dialog.ShowDialog() != true) return null;

        if (dialog.DeleteRequested)
        {
            DataStore.Training.Delete(t);
            Toaster.Show($"{t.Number} deleted.");
            return null;
        }

        t.Employee = dialog.Text("employee");
        t.Course = dialog.Text("course");
        t.CompletedOn = dialog.Date("completed") ?? DateTime.Today;
        t.ExpiresOn = dialog.Date("expires");
        t.Trainer = dialog.Text("trainer");
        t.Notes = dialog.Text("notes");

        if (t.ExpiresOn is { } exp && exp.Date < t.CompletedOn.Date)
            Toaster.Show("Heads up: the expiry date is before the completion date.");

        if (isNew) DataStore.Training.Add(t); else DataStore.Training.Update(t);
        if (!(t.ExpiresOn is { } e && e.Date < t.CompletedOn.Date))
            Toaster.Show(isNew ? $"{t.Number} saved." : $"{t.Number} updated.");
        return t;
    }

    // ------------------------------------------------------------------ Regulatory Compliance

    public static ComplianceItem? EditCompliance(ComplianceItem? existing)
    {
        var c = existing ?? new ComplianceItem();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New requirement" : $"Edit {c.Number}",
            isNew ? "A permit, license, report or inspection you must keep up with. Recurring ones roll forward when you mark them done."
                  : $"Added {c.CreatedAt:MMM d, yyyy}" + (c.LastCompleted is { } last ? $" · last done {last:MMM d, yyyy}" : ""),
            [
                new FormField { Key = "title", Label = "Requirement", Required = true, Value = c.Title, Hint = "For example: Stormwater permit renewal, Annual DOT vehicle inspection, OSHA 300A posting." },
                new FormField { Key = "agency", Label = "Agency", Kind = FieldKind.Choice, Options = ComplianceItem.Agencies, Value = c.Agency },
                new FormField { Key = "type", Label = "Type", Kind = FieldKind.Choice, Options = ComplianceItem.Types, Value = c.Type },
                new FormField { Key = "ref", Label = "Permit / license / reference number", Value = c.Reference },
                new FormField { Key = "due", Label = "Next due date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)c.DueDate },
                new FormField { Key = "freq", Label = "Repeats", Kind = FieldKind.Choice, Options = ComplianceItem.Frequencies, Value = c.Frequency },
                new FormField { Key = "location", Label = "Location", Value = c.Location },
                new FormField { Key = "owner", Label = "Owner", Value = c.Owner },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = c.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add requirement" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Compliance.Delete(c);
            Toaster.Show($"{c.Number} deleted.");
            return null;
        }

        var newDue = dialog.Date("due") ?? DateTime.Today;
        if (newDue.Date != c.DueDate.Date) c.IsCompleted = false; // a new due date reopens a finished one-time item
        c.Title = dialog.Text("title");
        c.Agency = dialog.Text("agency");
        c.Type = dialog.Text("type");
        c.Reference = dialog.Text("ref");
        c.DueDate = newDue;
        c.Frequency = dialog.Text("freq");
        c.Location = dialog.Text("location");
        c.Owner = dialog.Text("owner");
        c.Notes = dialog.Text("notes");

        if (isNew) DataStore.Compliance.Add(c); else DataStore.Compliance.Update(c);
        Toaster.Show(isNew ? $"{c.Number} added." : $"{c.Number} saved.");
        return c;
    }

    public static void MarkComplianceDone(ComplianceItem c)
    {
        c.MarkDone(DateTime.Today);
        DataStore.Compliance.Update(c);
        Toaster.Show(c.IsCompleted ? $"{c.Number} marked complete." : $"{c.Number} done. Next due {c.DueDate:MMM d, yyyy}.");
    }

    // ------------------------------------------------------------------ Claims Register

    public static Claim? EditClaim(Claim? existing)
    {
        var c = existing ?? new Claim();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New claim" : $"Edit {c.Number}",
            isNew ? "Track a workers' comp, auto, liability or property claim from first report to close."
                  : $"Opened {c.CreatedAt:MMM d, yyyy} · {c.DaysOpen} days" + (c.IsOpen ? " open" : ""),
            [
                new FormField { Key = "type", Label = "Claim type", Kind = FieldKind.Choice, Options = Claim.Types, Value = c.Type },
                new FormField { Key = "claimant", Label = "Claimant", Required = true, Value = c.Claimant, Hint = "The injured employee, driver or third party." },
                new FormField { Key = "loss", Label = "Date of loss", Kind = FieldKind.Date, Required = true, Value = (DateTime?)c.DateOfLoss },
                new FormField { Key = "location", Label = "Location", Value = c.Location },
                new FormField { Key = "incident", Label = "Related incident or accident", Value = c.RelatedIncident, Hint = "For example an incident number, or a short description." },
                new FormField { Key = "carrier", Label = "Insurance carrier", Value = c.Carrier },
                new FormField { Key = "carrierNo", Label = "Carrier claim number", Value = c.CarrierClaimNumber },
                new FormField { Key = "adjuster", Label = "Adjuster", Value = c.Adjuster, Hint = "Name, phone or email." },
                new FormField { Key = "reserve", Label = "Reserve", Kind = FieldKind.Number, Value = isNew ? null : c.Reserve, Hint = "Money still set aside for this claim." },
                new FormField { Key = "paid", Label = "Paid to date", Kind = FieldKind.Number, Value = isNew ? null : c.Paid },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = Claim.Statuses, Value = c.Status },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = c.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add claim" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Claims.Delete(c);
            Toaster.Show($"{c.Number} deleted.");
            return null;
        }

        string oldStatus = c.Status;
        c.Type = dialog.Text("type");
        c.Claimant = dialog.Text("claimant");
        c.DateOfLoss = dialog.Date("loss") ?? DateTime.Today;
        c.Location = dialog.Text("location");
        c.RelatedIncident = dialog.Text("incident");
        c.Carrier = dialog.Text("carrier");
        c.CarrierClaimNumber = dialog.Text("carrierNo");
        c.Adjuster = dialog.Text("adjuster");
        c.Reserve = dialog.Number("reserve") ?? 0;
        c.Paid = dialog.Number("paid") ?? 0;
        c.Status = dialog.Text("status");
        c.Notes = dialog.Text("notes");
        if (!c.IsOpen && oldStatus == Status.Open) c.ClosedOn = DateTime.Today;
        if (c.IsOpen) c.ClosedOn = null;

        if (isNew) DataStore.Claims.Add(c); else DataStore.Claims.Update(c);
        Toaster.Show(isNew ? $"{c.Number} added." : $"{c.Number} saved.");
        return c;
    }

    // ------------------------------------------------------------------ Library

    public static LibraryDocument? EditDocument(LibraryDocument? existing)
    {
        var d = existing ?? new LibraryDocument();
        bool isNew = existing is null;
        string? currentFile = d.HasFile ? Path.Combine(DataStore.Folder, d.StoredFile) : null;

        var dialog = new FormDialog(
            isNew ? "Add document" : $"Edit {d.Number}",
            isNew ? "A copy of the file is kept in Guardrail's data folder, so moving or deleting the original doesn't break it."
                  : $"Last updated {d.UpdatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "title", Label = "Title", Required = true, Value = d.Title, Hint = "For example: Hazard Communication Program, Lockout/Tagout Procedure." },
                new FormField { Key = "category", Label = "Category", Kind = FieldKind.Choice, Options = LibraryDocument.Categories, Value = d.Category },
                new FormField { Key = "file", Label = isNew ? "File" : "File (choose a new one to replace it)", Kind = FieldKind.File, Required = true, Value = currentFile },
                new FormField { Key = "version", Label = "Version", Value = d.DocVersion },
                new FormField { Key = "owner", Label = "Owner", Value = d.Owner },
                new FormField { Key = "review", Label = "Next review", Kind = FieldKind.Date, Value = d.ReviewDue, Hint = "Guardrail flags it 30 days before. Leave blank if it doesn't need regular review." },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = d.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add document" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Library.Delete(d);
            TryDeleteFolder(Path.Combine(DataStore.LibraryFolder, d.Id.ToString("N")));
            Toaster.Show($"{d.Number} deleted.");
            return null;
        }

        d.Title = dialog.Text("title");
        d.Category = dialog.Text("category");
        d.DocVersion = dialog.Text("version");
        d.Owner = dialog.Text("owner");
        d.ReviewDue = dialog.Date("review");
        d.Notes = dialog.Text("notes");

        string source = dialog.Text("file");
        if (!string.Equals(source, currentFile, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                d.StoredFile = StoreLibraryFile(d.Id, source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Toaster.Show($"Couldn't copy the file: {ex.Message}");
                return null;
            }
        }

        if (isNew) DataStore.Library.Add(d); else DataStore.Library.Update(d);
        Toaster.Show(isNew ? $"{d.Number} added." : $"{d.Number} saved.");
        return d;
    }

    /// <summary>Copies a file into data\library\&lt;id&gt;\ (replacing any earlier file) and returns its path relative to the data folder.</summary>
    public static string StoreLibraryFile(Guid id, string source)
    {
        var folder = Path.Combine(DataStore.LibraryFolder, id.ToString("N"));
        var target = Path.Combine(folder, Path.GetFileName(source));
        var temp = target + ".copying";
        Directory.CreateDirectory(folder);
        File.Copy(source, temp, overwrite: true);
        foreach (var old in Directory.GetFiles(folder))
            if (!old.Equals(temp, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        File.Move(temp, target);
        return Path.GetRelativePath(DataStore.Folder, target);
    }

    public static void OpenDocument(LibraryDocument d)
    {
        var path = Path.Combine(DataStore.Folder, d.StoredFile);
        if (!d.HasFile || !File.Exists(path))
        {
            Toaster.Show("The file for this document is missing. Edit it and choose the file again.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Toaster.Show($"Couldn't open {d.FileName}: {ex.Message}"); }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { /* ignore */ }
    }
}
