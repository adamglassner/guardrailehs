using System.Diagnostics;
using System.IO;
using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Add / edit / delete dialogs for each working module.</summary>
public static partial class Editors
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
    public static string StoreLibraryFile(Guid id, string source) => StoreFile("library", id, source);

    /// <summary>Copies a file into data\&lt;area&gt;\&lt;id&gt;\ (replacing any earlier file) and returns its path relative to the data folder.</summary>
    public static string StoreFile(string area, Guid id, string source)
    {
        var folder = Path.Combine(DataStore.Folder, area, id.ToString("N"));
        var target = Path.Combine(folder, Path.GetFileName(source));
        var temp = target + ".copying";
        Directory.CreateDirectory(folder);
        File.Copy(source, temp, overwrite: true);
        foreach (var old in Directory.GetFiles(folder))
            if (!old.Equals(temp, StringComparison.OrdinalIgnoreCase)) File.Delete(old);
        File.Move(temp, target);
        return Path.GetRelativePath(DataStore.Folder, target);
    }

    public static void OpenDocument(LibraryDocument d) => OpenStoredFile(d.StoredFile, "this document");

    /// <summary>Opens a file kept in the data folder with its default program.</summary>
    public static void OpenStoredFile(string storedFile, string what)
    {
        var path = Path.Combine(DataStore.Folder, storedFile);
        if (storedFile.Length == 0 || !File.Exists(path))
        {
            Toaster.Show($"There's no file for {what}. Edit it and choose the file.");
            return;
        }
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Toaster.Show($"Couldn't open {Path.GetFileName(path)}: {ex.Message}"); }
    }

    /// <summary>Stores a newly chosen file for a record. Returns false (and says why) if the copy failed.</summary>
    private static bool TryStoreChosenFile(string area, Guid id, string chosen, string? current, Action<string> setStored)
    {
        if (chosen.Length == 0 || string.Equals(chosen, current, StringComparison.OrdinalIgnoreCase)) return true;
        try
        {
            setStored(StoreFile(area, id, chosen));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Toaster.Show($"Couldn't copy the file: {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteFolder(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); } catch { /* ignore */ }
    }

    // ------------------------------------------------------------------ Monthly Data

    public static MonthlyEntry? EditMonthly(MonthlyEntry? existing)
    {
        var m = existing ?? new MonthlyEntry();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Enter a month" : $"Edit {m.MonthText}" + (m.Location.Length > 0 ? $" · {m.Location}" : ""),
            "Hours worked and headcount are what OSHA incident rates are based on (per 200,000 hours). Miles driven feed the vehicle accident rate.",
            [
                new FormField { Key = "month", Label = "Month", Kind = FieldKind.Date, Required = true, Value = (DateTime?)m.Month, Hint = "Pick any day in the month." },
                new FormField { Key = "location", Label = "Location", Value = m.Location, Hint = "Leave blank if you track the whole company as one." },
                new FormField { Key = "hours", Label = "Hours worked", Kind = FieldKind.Number, Required = true, Value = isNew ? null : m.HoursWorked, Hint = "All employees, including overtime. Not vacation or sick time." },
                new FormField { Key = "employees", Label = "Average number of employees", Kind = FieldKind.Number, Value = isNew ? null : m.Employees },
                new FormField { Key = "miles", Label = "Miles driven (fleet)", Kind = FieldKind.Number, Value = isNew ? null : m.MilesDriven },
                new FormField { Key = "by", Label = "Entered by", Value = m.EnteredBy },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = m.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Save month" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Monthly.Delete(m);
            Toaster.Show($"{m.MonthText} deleted.");
            return null;
        }

        var month = MonthlyEntry.FirstOfMonth(dialog.Date("month") ?? DateTime.Today);
        var location = dialog.Text("location");
        var duplicate = DataStore.Monthly.Items.FirstOrDefault(x => !ReferenceEquals(x, m) && x.Month == month
            && string.Equals(x.Location, location, StringComparison.OrdinalIgnoreCase));
        if (duplicate != null)
        {
            Toaster.Show($"{month:MMMM yyyy}{(location.Length > 0 ? " for " + location : "")} is already entered. Edit that row instead.");
            return null;
        }

        m.Month = month;
        m.Location = location;
        m.HoursWorked = dialog.Number("hours") ?? 0;
        m.Employees = dialog.Number("employees") ?? 0;
        m.MilesDriven = dialog.Number("miles") ?? 0;
        m.EnteredBy = dialog.Text("by");
        m.Notes = dialog.Text("notes");

        if (isNew) DataStore.Monthly.Add(m); else DataStore.Monthly.Update(m);
        Toaster.Show($"{m.MonthText} saved.");
        return m;
    }

    // ------------------------------------------------------------------ Behavioral Observations

    public static BbsObservation? EditBbs(BbsObservation? existing)
    {
        var b = existing ?? new BbsObservation();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New behavioral observation" : $"Edit {b.Number}",
            isNew ? "Watch a task for a few minutes, count safe and at-risk behaviors, then talk with the person about what you saw."
                  : $"Observed {b.DateText}" + (b.LinkedAction.Length > 0 ? $" · action {b.LinkedAction}" : ""),
            [
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)b.Date },
                new FormField { Key = "observer", Label = "Observer", Value = b.Observer },
                new FormField { Key = "area", Label = "Area / location", Value = b.Area },
                new FormField { Key = "task", Label = "Task observed", Required = true, Value = b.Task, Hint = "For example: Loading trailer, Changing saw blade, Pallet jack in aisle 4." },
                new FormField { Key = "focus", Label = "Focus area", Kind = FieldKind.Choice, Options = BbsObservation.FocusAreas, Value = b.FocusArea },
                new FormField { Key = "safe", Label = "Safe behaviors seen", Kind = FieldKind.Number, Value = isNew ? null : (decimal)b.SafeCount },
                new FormField { Key = "risk", Label = "At-risk behaviors seen", Kind = FieldKind.Number, Value = isNew ? null : (decimal)b.AtRiskCount },
                new FormField { Key = "feedback", Label = "Feedback given to the worker?", Kind = FieldKind.Choice, Options = BbsObservation.YesNo, Value = b.FeedbackGiven },
                new FormField { Key = "comments", Label = "What was seen / discussed", Kind = FieldKind.MultiLine, Value = b.Comments },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Save observation" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Bbs.Delete(b);
            Toaster.Show($"{b.Number} deleted.");
            return null;
        }

        b.Date = dialog.Date("date") ?? DateTime.Today;
        b.Observer = dialog.Text("observer");
        b.Area = dialog.Text("area");
        b.Task = dialog.Text("task");
        b.FocusArea = dialog.Text("focus");
        b.SafeCount = (int)Math.Round(dialog.Number("safe") ?? 0);
        b.AtRiskCount = (int)Math.Round(dialog.Number("risk") ?? 0);
        b.FeedbackGiven = dialog.Text("feedback");
        b.Comments = dialog.Text("comments");

        if (isNew) DataStore.Bbs.Add(b); else DataStore.Bbs.Update(b);
        Toaster.Show(isNew ? $"{b.Number} saved." : $"{b.Number} updated.");
        return b;
    }

    /// <summary>Turns the at-risk behaviors on a BBS card into a pre-filled, linked action.</summary>
    public static void CreateActionFrom(BbsObservation b)
    {
        var draft = new ActionItem
        {
            Title = $"Address at-risk behavior: {b.Task} ({b.FocusArea})",
            Description = $"From {b.Number} ({b.DateText}, {b.Area}): {b.AtRiskCount} at-risk / {b.SafeCount} safe.\n{b.Comments}".TrimEnd(),
            Location = b.Area,
            DueDate = DateTime.Today.AddDays(14),
            Source = b.Number,
        };
        var saved = EditAction(null, draft);
        if (saved is null) return;
        b.LinkedAction = saved.Number;
        DataStore.Bbs.Update(b);
    }

    // ------------------------------------------------------------------ Work Permits

    public static WorkPermit? EditPermit(WorkPermit? existing)
    {
        var w = existing ?? new WorkPermit();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Issue a work permit" : $"Edit {w.Number}",
            isNew ? "Record the job, who it's issued to, how long it's valid and the precautions that must be in place."
                  : $"Issued {w.CreatedAt:MMM d, yyyy}" + (w.ClosedOn is { } closed ? $" · closed {closed:MMM d, yyyy}" : ""),
            [
                new FormField { Key = "type", Label = "Permit type", Kind = FieldKind.Choice, Options = WorkPermit.Types, Value = w.Type },
                new FormField { Key = "work", Label = "Work to be done", Required = true, Value = w.Work },
                new FormField { Key = "location", Label = "Location", Required = true, Value = w.Location },
                new FormField { Key = "to", Label = "Issued to", Required = true, Value = w.IssuedTo, Hint = "The person or crew doing the work." },
                new FormField { Key = "by", Label = "Issued by", Value = w.IssuedBy },
                new FormField { Key = "from", Label = "Valid from", Kind = FieldKind.Date, Required = true, Value = (DateTime?)w.ValidFrom },
                new FormField { Key = "until", Label = "Valid until", Kind = FieldKind.Date, Required = true, Value = (DateTime?)w.ValidUntil },
                new FormField { Key = "precautions", Label = "Precautions / checks", Kind = FieldKind.MultiLine, Value = w.Precautions,
                                Hint = "For example: fire watch for 30 minutes after, gas test O2 20.9% LEL 0%, area barricaded, isolation LTO-0004." },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = WorkPermit.Statuses, Value = w.Status },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Issue permit" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Permits.Delete(w);
            Toaster.Show($"{w.Number} deleted.");
            return null;
        }

        var from = dialog.Date("from") ?? DateTime.Today;
        var until = dialog.Date("until") ?? from;
        if (until.Date < from.Date)
        {
            Toaster.Show("\"Valid until\" can't be before \"Valid from\". Nothing was saved.");
            return null;
        }

        string oldStatus = w.Status;
        w.Type = dialog.Text("type");
        w.Work = dialog.Text("work");
        w.Location = dialog.Text("location");
        w.IssuedTo = dialog.Text("to");
        w.IssuedBy = dialog.Text("by");
        w.ValidFrom = from;
        w.ValidUntil = until;
        w.Precautions = dialog.Text("precautions");
        w.Status = dialog.Text("status");
        if (!w.IsActive && oldStatus == Status.Active) w.ClosedOn = DateTime.Today;
        if (w.IsActive) w.ClosedOn = null;

        if (isNew) DataStore.Permits.Add(w); else DataStore.Permits.Update(w);
        Toaster.Show(isNew ? $"{w.Number} issued." : $"{w.Number} saved.");
        return w;
    }

    public static void ClosePermit(WorkPermit w)
    {
        w.Status = Status.Closed;
        w.ClosedOn = DateTime.Today;
        DataStore.Permits.Update(w);
        Toaster.Show($"{w.Number} closed out.");
    }

    // ------------------------------------------------------------------ Lockout / Tagout

    public static LotoProcedure? EditLoto(LotoProcedure? existing)
    {
        var l = existing ?? new LotoProcedure();
        bool isNew = existing is null;
        string? currentFile = l.HasFile ? Path.Combine(DataStore.Folder, l.StoredFile) : null;

        var dialog = new FormDialog(
            isNew ? "New LOTO procedure" : $"Edit {l.Number}",
            isNew ? "An energy control procedure for one machine. OSHA requires a periodic inspection of each procedure at least once a year."
                  : $"Added {l.CreatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "equipment", Label = "Equipment", Required = true, Value = l.Equipment, Hint = "For example: Baler #2, Conveyor C-14, Air compressor." },
                new FormField { Key = "tag", Label = "Asset tag / ID", Value = l.AssetTag },
                new FormField { Key = "location", Label = "Location", Value = l.Location },
                new FormField { Key = "proc", Label = "Procedure number", Value = l.ProcedureNumber },
                new FormField { Key = "energy", Label = "Energy sources", Kind = FieldKind.MultiLine, Value = l.EnergySources, Hint = "For example: Electrical 480 V, Pneumatic 120 psi, Hydraulic, Gravity (raised platen)." },
                new FormField { Key = "points", Label = "Isolation points and steps", Kind = FieldKind.MultiLine, Value = l.IsolationPoints },
                new FormField { Key = "file", Label = "Procedure document or photo (optional)", Kind = FieldKind.File, Value = currentFile },
                new FormField { Key = "last", Label = "Last periodic inspection", Kind = FieldKind.Date, Value = l.LastInspection },
                new FormField { Key = "inspector", Label = "Inspected by", Value = l.Inspector },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add procedure" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Loto.Delete(l);
            TryDeleteFolder(Path.Combine(DataStore.Folder, "loto", l.Id.ToString("N")));
            Toaster.Show($"{l.Number} deleted.");
            return null;
        }

        if (!TryStoreChosenFile("loto", l.Id, dialog.Text("file"), currentFile, f => l.StoredFile = f)) return null;
        l.Equipment = dialog.Text("equipment");
        l.AssetTag = dialog.Text("tag");
        l.Location = dialog.Text("location");
        l.ProcedureNumber = dialog.Text("proc");
        l.EnergySources = dialog.Text("energy");
        l.IsolationPoints = dialog.Text("points");
        l.LastInspection = dialog.Date("last");
        l.Inspector = dialog.Text("inspector");

        if (isNew) DataStore.Loto.Add(l); else DataStore.Loto.Update(l);
        Toaster.Show(isNew ? $"{l.Number} added." : $"{l.Number} saved.");
        return l;
    }

    public static void RecordLotoInspection(LotoProcedure l)
    {
        l.LastInspection = DateTime.Today;
        DataStore.Loto.Update(l);
        Toaster.Show($"{l.Number} inspected today. Next one is due {l.NextText}.");
    }

    // ------------------------------------------------------------------ Safety Data Sheets

    public static SdsEntry? EditSds(SdsEntry? existing)
    {
        var e = existing ?? new SdsEntry();
        bool isNew = existing is null;
        string? currentFile = e.HasFile ? Path.Combine(DataStore.Folder, e.StoredFile) : null;

        var dialog = new FormDialog(
            isNew ? "Add a product" : $"Edit {e.Number}",
            isNew ? "Every hazardous product on site needs its Safety Data Sheet available to workers. Attach the manufacturer's SDS (usually a PDF)."
                  : $"Added {e.CreatedAt:MMM d, yyyy}",
            [
                new FormField { Key = "product", Label = "Product name", Required = true, Value = e.Product },
                new FormField { Key = "maker", Label = "Manufacturer", Value = e.Manufacturer },
                new FormField { Key = "code", Label = "Product code", Value = e.ProductCode },
                new FormField { Key = "file", Label = "Safety Data Sheet", Kind = FieldKind.File, Value = currentFile },
                new FormField { Key = "signal", Label = "Signal word (section 2 of the SDS)", Kind = FieldKind.Choice, Options = SdsEntry.SignalWords, Value = e.SignalWord },
                new FormField { Key = "hazards", Label = "Hazards", Value = e.Hazards, Hint = "For example: Flammable liquid, Skin irritant, Corrosive." },
                new FormField { Key = "location", Label = "Where it's used or stored", Value = e.Location },
                new FormField { Key = "qty", Label = "Quantity on hand", Value = e.Quantity, Hint = "For example: 4 x 1 gal, 55 gal drum." },
                new FormField { Key = "rev", Label = "SDS revision date", Kind = FieldKind.Date, Value = e.RevisionDate },
                new FormField { Key = "notes", Label = "Notes", Kind = FieldKind.MultiLine, Value = e.Notes },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Add product" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Sds.Delete(e);
            TryDeleteFolder(Path.Combine(DataStore.Folder, "sds", e.Id.ToString("N")));
            Toaster.Show($"{e.Number} deleted.");
            return null;
        }

        if (!TryStoreChosenFile("sds", e.Id, dialog.Text("file"), currentFile, f => e.StoredFile = f)) return null;
        e.Product = dialog.Text("product");
        e.Manufacturer = dialog.Text("maker");
        e.ProductCode = dialog.Text("code");
        e.SignalWord = dialog.Text("signal");
        e.Hazards = dialog.Text("hazards");
        e.Location = dialog.Text("location");
        e.Quantity = dialog.Text("qty");
        e.RevisionDate = dialog.Date("rev");
        e.Notes = dialog.Text("notes");

        if (isNew) DataStore.Sds.Add(e); else DataStore.Sds.Update(e);
        Toaster.Show(isNew ? $"{e.Number} added." + (e.HasFile ? "" : " Attach its SDS when you have it.") : $"{e.Number} saved.");
        return e;
    }
}
