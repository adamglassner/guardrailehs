using Guardrail.Data;
using Guardrail.Services;

namespace Guardrail.Views;

/// <summary>Editors for Incidents, Audits &amp; Inspections, Risk Assessment and Management of Change.</summary>
public static partial class Editors
{
    // ------------------------------------------------------------------ Incidents

    public static Incident? EditIncident(Incident? existing)
    {
        var i = existing ?? new Incident();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Report an incident" : $"Edit {i.Number}",
            isNew ? "Capture what happened now; you can add the investigation and OSHA details later."
                  : $"Reported {i.CreatedAt:MMM d, yyyy}" + (i.HasLinkedActions ? $" · actions {i.LinkedActions}" : ""),
            [
                new FormField { Key = "type", Label = "What type of incident?", Kind = FieldKind.Choice, Options = Incident.Types, Value = i.Type },
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)i.Date },
                new FormField { Key = "time", Label = "Time", Value = i.Time, Hint = "For example 2:15 PM." },
                new FormField { Key = "location", Label = "Where did it happen?", Required = true, Value = i.Location },
                new FormField { Key = "desc", Label = "What happened?", Kind = FieldKind.MultiLine, Required = true, Value = i.Description },
                new FormField { Key = "person", Label = "Person involved", Value = i.PersonInvolved },
                new FormField { Key = "job", Label = "Their job title", Value = i.JobTitle },
                new FormField { Key = "by", Label = "Reported by", Value = i.ReportedBy },
                new FormField { Key = "class", Label = "OSHA classification (injury / illness only)", Kind = FieldKind.Choice, Options = Incident.Classifications, Value = i.Classification,
                                Hint = "Medical treatment beyond first aid, restricted work or job transfer, days away and fatalities are recordable on the OSHA 300 log." },
                new FormField { Key = "case", Label = "Injury or illness type (OSHA 300 column M)", Kind = FieldKind.Choice, Options = Incident.CaseTypes, Value = i.CaseType },
                new FormField { Key = "body", Label = "Body part affected", Value = i.BodyPart },
                new FormField { Key = "away", Label = "Days away from work", Kind = FieldKind.Number, Value = isNew ? null : (decimal)i.DaysAway },
                new FormField { Key = "restricted", Label = "Days on restricted work or job transfer", Kind = FieldKind.Number, Value = isNew ? null : (decimal)i.DaysRestricted },
                new FormField { Key = "dot", Label = "DOT recordable crash? (motor vehicle only)", Kind = FieldKind.Choice, Options = Incident.YesNo, Value = i.DotRecordable,
                                Hint = "A fatality, an injury treated away from the scene, or a vehicle towed away." },
                new FormField { Key = "immediate", Label = "Immediate actions taken", Kind = FieldKind.MultiLine, Value = i.ImmediateActions },
                new FormField { Key = "cause", Label = "Root cause (investigation)", Kind = FieldKind.MultiLine, Value = i.RootCause },
                new FormField { Key = "status", Label = "Status", Kind = FieldKind.Choice, Options = Incident.Statuses, Value = i.Status },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Submit report" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Incidents.Delete(i);
            Toaster.Show($"{i.Number} deleted.");
            return null;
        }

        i.Type = dialog.Text("type");
        i.Date = dialog.Date("date") ?? DateTime.Today;
        i.Time = dialog.Text("time");
        i.Location = dialog.Text("location");
        i.Description = dialog.Text("desc");
        i.PersonInvolved = dialog.Text("person");
        i.JobTitle = dialog.Text("job");
        i.ReportedBy = dialog.Text("by");
        i.Classification = dialog.Text("class");
        i.CaseType = dialog.Text("case");
        i.BodyPart = dialog.Text("body");
        i.DaysAway = (int)Math.Round(dialog.Number("away") ?? 0);
        i.DaysRestricted = (int)Math.Round(dialog.Number("restricted") ?? 0);
        i.DotRecordable = dialog.Text("dot");
        i.ImmediateActions = dialog.Text("immediate");
        i.RootCause = dialog.Text("cause");
        i.Status = dialog.Text("status");

        if (isNew) DataStore.Incidents.Add(i); else DataStore.Incidents.Update(i);
        Toaster.Show(isNew ? $"{i.Number} reported." + (i.IsRecordable ? " It's OSHA recordable." : "") : $"{i.Number} saved.");
        return i;
    }

    public static void CreateActionFrom(Incident i)
    {
        var draft = new ActionItem
        {
            Title = $"Corrective action for {i.Number}: {Shorten(i.Description, 60)}",
            Description = $"From {i.Number} ({i.Type}, {i.DateText}, {i.Location}).\n{i.Description}" +
                          (i.RootCause.Length > 0 ? $"\nRoot cause: {i.RootCause}" : ""),
            Location = i.Location,
            DueDate = DateTime.Today.AddDays(14),
            Priority = i.IsRecordable ? "High" : "Medium",
            Source = i.Number,
        };
        var saved = EditAction(null, draft);
        if (saved is null) return;
        i.LinkedActions = i.LinkedActions.Length == 0 ? saved.Number : $"{i.LinkedActions}, {saved.Number}";
        if (i.Status == Status.Open) i.Status = Status.UnderInvestigation;
        DataStore.Incidents.Update(i);
    }

    private static string Shorten(string text, int max)
    {
        text = text.ReplaceLineEndings(" ").Trim();
        return text.Length <= max ? text : text[..(max - 1)].TrimEnd() + "…";
    }

    // ------------------------------------------------------------------ Audits & Inspections

    public static InspectionTemplate? EditTemplate(InspectionTemplate? existing)
    {
        var t = existing ?? new InspectionTemplate();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New checklist" : $"Edit checklist: {t.Name}",
            "One item per line. Each item is answered Pass, Fail or N/A during an inspection.",
            [
                new FormField { Key = "name", Label = "Checklist name", Required = true, Value = t.Name },
                new FormField { Key = "desc", Label = "Description", Value = t.Description },
                new FormField { Key = "items", Label = "Items (one per line)", Kind = FieldKind.MultiLine, Required = true, Value = string.Join(Environment.NewLine, t.Items) },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Create checklist" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Templates.Delete(t);
            Toaster.Show($"Checklist \"{t.Name}\" deleted. Past inspections keep their answers.");
            return null;
        }

        t.Name = dialog.Text("name");
        t.Description = dialog.Text("desc");
        t.Items = dialog.Text("items").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        if (isNew) DataStore.Templates.Add(t); else DataStore.Templates.Update(t);
        Toaster.Show($"Checklist \"{t.Name}\" saved ({t.ItemCountText}).");
        return t;
    }

    /// <summary>Starts a new inspection: pick the checklist and where, then fill it in.</summary>
    public static void StartInspection()
    {
        var templates = DataStore.Templates.Items.OrderBy(t => t.Name).ToList();
        if (templates.Count == 0)
        {
            Toaster.Show("Create a checklist first (Checklists button).");
            return;
        }

        var dialog = new FormDialog(
            "Start an inspection",
            "Pick the checklist, then answer each item Pass, Fail or N/A.",
            [
                new FormField { Key = "template", Label = "Checklist", Kind = FieldKind.Choice, Options = templates.Select(t => t.Name).ToArray(), Value = templates[0].Name },
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)DateTime.Today },
                new FormField { Key = "location", Label = "Location / area", Required = true },
                new FormField { Key = "inspector", Label = "Inspector" },
            ],
            allowDelete: false,
            saveText: "Start");
        if (dialog.ShowDialog() != true) return;

        var template = templates.First(t => t.Name == dialog.Text("template"));
        var inspection = new Inspection
        {
            TemplateName = template.Name,
            Date = dialog.Date("date") ?? DateTime.Today,
            Location = dialog.Text("location"),
            Inspector = dialog.Text("inspector"),
            Items = template.Items.Select(text => new ChecklistItem { Text = text }).ToList(),
        };
        DataStore.Inspections.Add(inspection);
        FillInspection(inspection);
    }

    /// <summary>Opens the checklist for answering (or reviewing).</summary>
    public static void FillInspection(Inspection inspection)
    {
        var window = new ChecklistWindow(inspection);
        if (window.ShowDialog() != true) return;

        if (window.DeleteRequested)
        {
            DataStore.Inspections.Delete(inspection);
            Toaster.Show($"{inspection.Number} deleted.");
            return;
        }
        DataStore.Inspections.Update(inspection);
        Toaster.Show(inspection.IsComplete
            ? $"{inspection.Number} completed: score {inspection.ScoreText}, {inspection.FindingsText.ToLowerInvariant()}."
            : $"{inspection.Number} saved as in progress ({inspection.Unanswered} items left).");
    }

    /// <summary>Creates one action per failed checklist item, linked back to the inspection.</summary>
    public static void CreateActionsFrom(Inspection inspection)
    {
        var created = new List<string>();
        foreach (var item in inspection.Items.Where(i => i.Result == ChecklistItem.Fail))
        {
            var action = new ActionItem
            {
                Title = $"Fix: {Shorten(item.Text, 70)}",
                Description = $"Failed on {inspection.Number} ({inspection.TemplateName}, {inspection.DateText}, {inspection.Location})." +
                              (item.Note.Length > 0 ? $"\n{item.Note}" : ""),
                Location = inspection.Location,
                Owner = inspection.Inspector,
                DueDate = DateTime.Today.AddDays(14),
                Source = inspection.Number,
            };
            DataStore.Actions.Add(action);
            created.Add(action.Number);
        }
        if (created.Count == 0) return;
        inspection.LinkedActions = string.Join(", ", created);
        DataStore.Inspections.Update(inspection);
        Toaster.Show($"Created {created.Count} action{(created.Count == 1 ? "" : "s")}: {inspection.LinkedActions}.");
    }

    // ------------------------------------------------------------------ Risk Assessment

    public static RiskAssessment? EditRisk(RiskAssessment? existing)
    {
        var r = existing ?? new RiskAssessment();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "New risk assessment" : $"Edit {r.Number}",
            "Rate the risk before controls, list the controls, then rate what's left. Score = likelihood × severity: 1–4 Low, 5–9 Medium, 10–14 High, 15–25 Extreme.",
            [
                new FormField { Key = "task", Label = "Task or activity", Required = true, Value = r.Task, Hint = "For example: Changing conveyor belt, Working on roof, Unloading drums." },
                new FormField { Key = "hazard", Label = "Hazard", Kind = FieldKind.MultiLine, Required = true, Value = r.Hazard, Hint = "What could hurt someone, and how?" },
                new FormField { Key = "location", Label = "Location", Value = r.Location },
                new FormField { Key = "assessor", Label = "Assessed by", Value = r.Assessor },
                new FormField { Key = "date", Label = "Date", Kind = FieldKind.Date, Required = true, Value = (DateTime?)r.Date },
                new FormField { Key = "l", Label = "Likelihood (before controls)", Kind = FieldKind.Choice, Options = RiskAssessment.LikelihoodLabels, Value = RiskAssessment.LikelihoodLabels[r.Likelihood - 1] },
                new FormField { Key = "s", Label = "Severity (before controls)", Kind = FieldKind.Choice, Options = RiskAssessment.SeverityLabels, Value = RiskAssessment.SeverityLabels[r.Severity - 1] },
                new FormField { Key = "controls", Label = "Controls", Kind = FieldKind.MultiLine, Value = r.Controls,
                                Hint = "Best first: eliminate, substitute, engineering controls, administrative controls, then PPE." },
                new FormField { Key = "rl", Label = "Likelihood (with controls)", Kind = FieldKind.Choice, Options = RiskAssessment.LikelihoodLabels, Value = RiskAssessment.LikelihoodLabels[r.ResidualLikelihood - 1] },
                new FormField { Key = "rs", Label = "Severity (with controls)", Kind = FieldKind.Choice, Options = RiskAssessment.SeverityLabels, Value = RiskAssessment.SeverityLabels[r.ResidualSeverity - 1] },
                new FormField { Key = "review", Label = "Next review", Kind = FieldKind.Date, Value = r.ReviewDue ?? (isNew ? DateTime.Today.AddYears(1) : (DateTime?)null) },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Save assessment" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Risks.Delete(r);
            Toaster.Show($"{r.Number} deleted.");
            return null;
        }

        r.Task = dialog.Text("task");
        r.Hazard = dialog.Text("hazard");
        r.Location = dialog.Text("location");
        r.Assessor = dialog.Text("assessor");
        r.Date = dialog.Date("date") ?? DateTime.Today;
        r.Likelihood = RiskAssessment.ParseRating(dialog.Text("l"));
        r.Severity = RiskAssessment.ParseRating(dialog.Text("s"));
        r.Controls = dialog.Text("controls");
        r.ResidualLikelihood = RiskAssessment.ParseRating(dialog.Text("rl"));
        r.ResidualSeverity = RiskAssessment.ParseRating(dialog.Text("rs"));
        r.ReviewDue = dialog.Date("review");

        if (isNew) DataStore.Risks.Add(r); else DataStore.Risks.Update(r);
        Toaster.Show($"{r.Number} saved: {r.Level} before controls, {r.ResidualLevel} after.");
        return r;
    }

    // ------------------------------------------------------------------ Management of Change

    public static ChangeRequest? EditChange(ChangeRequest? existing)
    {
        var c = existing ?? new ChangeRequest();
        bool isNew = existing is null;

        var dialog = new FormDialog(
            isNew ? "Request a change" : $"Edit {c.Number}",
            isNew ? "Describe the change so its safety impact can be reviewed and approved before it goes live."
                  : $"Requested {c.CreatedAt:MMM d, yyyy}" + (c.StageChangedOn is { } d ? $" · {c.Stage.ToLowerInvariant()} since {d:MMM d}" : ""),
            [
                new FormField { Key = "title", Label = "Change", Required = true, Value = c.Title, Hint = "For example: Replace solvent cleaner with water-based, New conveyor on line 3." },
                new FormField { Key = "type", Label = "Type of change", Kind = FieldKind.Choice, Options = ChangeRequest.Types, Value = c.Type },
                new FormField { Key = "desc", Label = "What will change?", Kind = FieldKind.MultiLine, Required = true, Value = c.Description },
                new FormField { Key = "reason", Label = "Why?", Kind = FieldKind.MultiLine, Value = c.Reason },
                new FormField { Key = "location", Label = "Location", Value = c.Location },
                new FormField { Key = "requester", Label = "Requested by", Value = c.Requester },
                new FormField { Key = "approver", Label = "Approver", Value = c.Approver },
                new FormField { Key = "duration", Label = "Permanent or temporary?", Kind = FieldKind.Choice, Options = ChangeRequest.Durations, Value = c.Duration },
                new FormField { Key = "until", Label = "Temporary until", Kind = FieldKind.Date, Value = c.TemporaryUntil, Hint = "Only for temporary changes. Guardrail flags it when the date passes." },
                new FormField { Key = "target", Label = "Target date", Kind = FieldKind.Date, Value = c.TargetDate },
                new FormField { Key = "risk", Label = "Risk assessment done?", Kind = FieldKind.Choice, Options = ChangeRequest.YesNo, Value = c.RiskReviewed },
                new FormField { Key = "training", Label = "Training or procedure updates needed?", Kind = FieldKind.Choice, Options = ChangeRequest.YesNo, Value = c.TrainingNeeded },
                new FormField { Key = "stage", Label = "Stage", Kind = FieldKind.Choice, Options = ChangeRequest.Stages, Value = c.Stage },
            ],
            allowDelete: !isNew,
            saveText: isNew ? "Submit request" : "Save changes");

        if (dialog.ShowDialog() != true) return null;
        if (dialog.DeleteRequested)
        {
            DataStore.Changes.Delete(c);
            Toaster.Show($"{c.Number} deleted.");
            return null;
        }

        string oldStage = c.Stage;
        c.Title = dialog.Text("title");
        c.Type = dialog.Text("type");
        c.Description = dialog.Text("desc");
        c.Reason = dialog.Text("reason");
        c.Location = dialog.Text("location");
        c.Requester = dialog.Text("requester");
        c.Approver = dialog.Text("approver");
        c.Duration = dialog.Text("duration");
        c.TemporaryUntil = c.IsTemporary ? dialog.Date("until") : null;
        c.TargetDate = dialog.Date("target");
        c.RiskReviewed = dialog.Text("risk");
        c.TrainingNeeded = dialog.Text("training");
        c.Stage = dialog.Text("stage");
        if (isNew || c.Stage != oldStage) c.StageChangedOn = DateTime.Today;

        if (isNew) DataStore.Changes.Add(c); else DataStore.Changes.Update(c);
        Toaster.Show(isNew ? $"{c.Number} submitted." : $"{c.Number} saved.");
        return c;
    }

    /// <summary>Moves a change to the next stage (Requested → Under review → Approved → Implemented → Closed).</summary>
    public static void AdvanceChange(ChangeRequest c)
    {
        if (c.NextStage is not { } next) return;
        if (next == Status.Approved && c.RiskReviewed != "Yes")
        {
            Toaster.Show($"{c.Number} needs a risk assessment before approval. Edit it and set \"Risk assessment done?\" to Yes.");
            return;
        }
        c.Stage = next;
        c.StageChangedOn = DateTime.Today;
        DataStore.Changes.Update(c);
        Toaster.Show($"{c.Number} is now {next.ToLowerInvariant()}.");
    }
}
