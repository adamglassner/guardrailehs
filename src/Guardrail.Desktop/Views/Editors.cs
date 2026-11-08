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
}
