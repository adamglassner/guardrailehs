using System.Text.Json.Serialization;

namespace Guardrail.Data;

/// <summary>A workplace incident: injury or illness, environmental release, property damage, vehicle, liability or near miss.</summary>
public sealed class Incident : RecordBase
{
    public const string InjuryIllness = "Injury / illness";
    public const string MotorVehicle = "Motor vehicle";
    public const string NearMiss = "Near miss";

    public static readonly string[] Types = [InjuryIllness, "Environmental", "Property damage", MotorVehicle, "Operational liability", NearMiss];
    public static readonly string[] Statuses = [Data.Status.Open, Data.Status.UnderInvestigation, Data.Status.Closed];

    // OSHA classification (Form 300 columns G–J). Only injury / illness cases can be recordable.
    public const string NotRecordable = "Not recordable";
    public const string FirstAid = "First aid only";
    public const string MedicalTreatment = "Medical treatment";
    public const string RestrictedTransfer = "Restricted / transfer";
    public const string DaysAwayCase = "Days away";
    public const string Fatality = "Fatality";
    public static readonly string[] Classifications = [NotRecordable, FirstAid, MedicalTreatment, RestrictedTransfer, DaysAwayCase, Fatality];

    /// <summary>OSHA Form 300 column M.</summary>
    public static readonly string[] CaseTypes = ["Injury", "Skin disorder", "Respiratory condition", "Poisoning", "Hearing loss", "All other illnesses"];
    public static readonly string[] YesNo = ["No", "Yes"];

    public DateTime Date { get; set; } = DateTime.Today;
    public string Time { get; set; } = "";
    public string Type { get; set; } = InjuryIllness;
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public string ReportedBy { get; set; } = "";
    public string PersonInvolved { get; set; } = "";
    public string JobTitle { get; set; } = "";
    public string Classification { get; set; } = NotRecordable;
    public string CaseType { get; set; } = "Injury";
    public string BodyPart { get; set; } = "";
    public int DaysAway { get; set; }
    public int DaysRestricted { get; set; }
    public string DotRecordable { get; set; } = "No";
    public string RootCause { get; set; } = "";
    public string ImmediateActions { get; set; } = "";
    public string Status { get; set; } = Data.Status.Open;
    /// <summary>Numbers of actions created from this incident, comma separated.</summary>
    public string LinkedActions { get; set; } = "";

    [JsonIgnore] public override string Prefix => "INC";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public bool IsInjuryIllness => Type == InjuryIllness;
    [JsonIgnore] public bool IsVehicle => Type == MotorVehicle;
    [JsonIgnore] public bool IsRecordable => IsInjuryIllness && Classification is MedicalTreatment or RestrictedTransfer or DaysAwayCase or Fatality;
    /// <summary>Days away, restricted or transferred (DART) case.</summary>
    [JsonIgnore] public bool IsDart => IsInjuryIllness && Classification is RestrictedTransfer or DaysAwayCase;
    /// <summary>Lost-time case: days away from work.</summary>
    [JsonIgnore] public bool IsLostTime => IsInjuryIllness && Classification == DaysAwayCase;
    [JsonIgnore] public string RecordableText => IsRecordable ? "Recordable" : IsInjuryIllness ? Classification : "—";
    [JsonIgnore] public bool HasLinkedActions => LinkedActions.Length > 0;
}

/// <summary>A reusable checklist, e.g. "Monthly workplace inspection".</summary>
public sealed class InspectionTemplate : RecordBase
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Items { get; set; } = [];

    [JsonIgnore] public override string Prefix => "TPL";
    [JsonIgnore] public string ItemCountText => $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";

    /// <summary>Starter checklists added the first time the Audits & Inspections page opens.</summary>
    public static List<InspectionTemplate> Starters() =>
    [
        new()
        {
            Name = "Monthly workplace inspection",
            Description = "General walk-through of a work area.",
            Items =
            [
                "Aisles, walkways and exits clear and marked",
                "Exit signs lit; emergency lighting works",
                "Fire extinguishers in place, charged and inspected this month",
                "Eyewash stations and first aid kits stocked and accessible",
                "Electrical panels clear (36 in.) and closed; no damaged cords",
                "Floors clean and dry; no slip or trip hazards",
                "Machine guards in place",
                "Chemicals labeled and stored properly; SDS available",
                "Ladders and step stools in good condition",
                "Workers wearing required PPE",
            ],
        },
        new()
        {
            Name = "Forklift pre-use check",
            Description = "Operator check before each shift (OSHA 1910.178(q)(7)).",
            Items =
            [
                "Forks, chains and mast free of damage",
                "Tires and wheels in good condition",
                "No hydraulic, fuel or oil leaks",
                "Horn works",
                "Lights and backup alarm work",
                "Service and parking brakes work",
                "Steering works properly",
                "Seat belt present and works",
                "Data plate legible",
                "Battery / LP tank secure and connections tight",
            ],
        },
        new()
        {
            Name = "Fire extinguisher monthly check",
            Description = "Monthly visual inspection (NFPA 10).",
            Items =
            [
                "In its designated place and visible",
                "Access not blocked",
                "Pressure gauge in the green",
                "Pin and tamper seal intact",
                "No physical damage, corrosion or leaks",
                "Operating instructions legible and facing out",
                "Inspection tag initialed and dated",
            ],
        },
    ];
}

public sealed class ChecklistItem
{
    public const string Pass = "Pass";
    public const string Fail = "Fail";
    public const string NotApplicable = "N/A";
    public static readonly string[] Results = [Pass, Fail, NotApplicable];

    public string Text { get; set; } = "";
    /// <summary>"Pass", "Fail", "N/A" or "" (not answered yet).</summary>
    public string Result { get; set; } = "";
    public string Note { get; set; } = "";
}

/// <summary>One completed (or in-progress) audit or inspection from a template.</summary>
public sealed class Inspection : RecordBase
{
    public string TemplateName { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public string Location { get; set; } = "";
    public string Inspector { get; set; } = "";
    public List<ChecklistItem> Items { get; set; } = [];
    public bool IsComplete { get; set; }
    public string Notes { get; set; } = "";
    /// <summary>Numbers of actions created from failed items, comma separated.</summary>
    public string LinkedActions { get; set; } = "";

    [JsonIgnore] public override string Prefix => "AUD";
    [JsonIgnore] public string DateText => Date.ToString("MMM d, yyyy");
    [JsonIgnore] public int Passed => Items.Count(i => i.Result == ChecklistItem.Pass);
    [JsonIgnore] public int Failed => Items.Count(i => i.Result == ChecklistItem.Fail);
    [JsonIgnore] public int Unanswered => Items.Count(i => i.Result.Length == 0);
    /// <summary>Pass ÷ (pass + fail); N/A items don't count.</summary>
    [JsonIgnore] public double? Score => Passed + Failed == 0 ? null : (double)Passed / (Passed + Failed);
    [JsonIgnore] public string ScoreText => Score is { } s ? $"{s * 100:0}%" : "—";
    [JsonIgnore] public string FindingsText => Failed == 0 ? "None" : $"{Failed} finding{(Failed == 1 ? "" : "s")}";
    [JsonIgnore] public string DisplayStatus => !IsComplete ? Status.InProgress : Failed > 0 ? Status.FindingsOpen : Status.Completed;
    [JsonIgnore] public bool CanCreateActions => IsComplete && Failed > 0 && LinkedActions.Length == 0;
    [JsonIgnore] public bool HasLinkedActions => LinkedActions.Length > 0;
}

/// <summary>A risk assessment (JHA / JSA style) scored on a 5 × 5 likelihood × severity matrix.</summary>
public sealed class RiskAssessment : RecordBase
{
    public static readonly string[] LikelihoodLabels = ["1 – Rare", "2 – Unlikely", "3 – Possible", "4 – Likely", "5 – Almost certain"];
    public static readonly string[] SeverityLabels = ["1 – Insignificant", "2 – Minor", "3 – Moderate", "4 – Major", "5 – Catastrophic"];

    public string Task { get; set; } = "";
    public string Hazard { get; set; } = "";
    public string Location { get; set; } = "";
    public string Assessor { get; set; } = "";
    public DateTime Date { get; set; } = DateTime.Today;
    public int Likelihood { get; set; } = 3;
    public int Severity { get; set; } = 3;
    public string Controls { get; set; } = "";
    public int ResidualLikelihood { get; set; } = 2;
    public int ResidualSeverity { get; set; } = 3;
    public DateTime? ReviewDue { get; set; }

    [JsonIgnore] public override string Prefix => "RSK";
    [JsonIgnore] public int Score => Likelihood * Severity;
    [JsonIgnore] public int ResidualScore => ResidualLikelihood * ResidualSeverity;
    [JsonIgnore] public string Level => LevelFor(Score);
    [JsonIgnore] public string ResidualLevel => LevelFor(ResidualScore);
    [JsonIgnore] public string ScoreText => $"{Score} · {Level}";
    [JsonIgnore] public string ResidualText => $"{ResidualScore} · {ResidualLevel}";
    [JsonIgnore] public string ReviewText => ReviewDue?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public bool ReviewIsDue => ReviewDue is { } d && d.Date <= DateTime.Today.AddDays(30);

    /// <summary>1–4 Low, 5–9 Medium, 10–14 High, 15–25 Extreme.</summary>
    public static string LevelFor(int score) => score switch
    {
        <= 4 => Status.Low,
        <= 9 => Status.Medium,
        <= 14 => Status.High,
        _ => Status.Extreme,
    };

    /// <summary>"3 – Possible" → 3.</summary>
    public static int ParseRating(string label) =>
        label.Length > 0 && char.IsDigit(label[0]) ? Math.Clamp(label[0] - '0', 1, 5) : 3;
}

/// <summary>A management-of-change (MOC) request: reviewed for safety before the change goes live.</summary>
public sealed class ChangeRequest : RecordBase
{
    public static readonly string[] Types = ["Process", "Equipment", "Material / chemical", "People / organization", "Procedure", "Facility"];
    public static readonly string[] Stages = [Status.Requested, Status.UnderReview, Status.Approved, Status.Implemented, Status.Closed, Status.Rejected];
    public static readonly string[] Durations = ["Permanent", "Temporary"];
    public static readonly string[] YesNo = ["No", "Yes"];

    public string Title { get; set; } = "";
    public string Type { get; set; } = "Equipment";
    public string Description { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Location { get; set; } = "";
    public string Requester { get; set; } = "";
    public string Approver { get; set; } = "";
    public string Duration { get; set; } = "Permanent";
    public DateTime? TemporaryUntil { get; set; }
    public DateTime? TargetDate { get; set; }
    public string RiskReviewed { get; set; } = "No";
    public string TrainingNeeded { get; set; } = "No";
    public string Stage { get; set; } = Status.Requested;
    public DateTime? StageChangedOn { get; set; }

    [JsonIgnore] public override string Prefix => "MOC";
    [JsonIgnore] public bool IsOpen => Stage is not (Status.Closed or Status.Rejected);
    [JsonIgnore] public bool IsTemporary => Duration == "Temporary";
    [JsonIgnore] public bool TemporaryExpired => IsTemporary && IsOpen && TemporaryUntil is { } d && d.Date < DateTime.Today;
    [JsonIgnore] public string DisplayStatus => TemporaryExpired ? Status.TemporaryExpired : Stage;
    [JsonIgnore] public string TargetText => TargetDate?.ToString("MMM d, yyyy") ?? "—";
    [JsonIgnore] public string DurationText => IsTemporary ? (TemporaryUntil is { } d ? $"Temporary until {d:MMM d}" : "Temporary") : "Permanent";

    /// <summary>The next stage in the normal flow, or null at the end.</summary>
    [JsonIgnore]
    public string? NextStage => Stage switch
    {
        Status.Requested => Status.UnderReview,
        Status.UnderReview => Status.Approved,
        Status.Approved => Status.Implemented,
        Status.Implemented => Status.Closed,
        _ => null,
    };
    [JsonIgnore] public bool CanAdvance => NextStage != null;
    [JsonIgnore] public string AdvanceText => NextStage is { } n ? $"→ {n}" : "";
}
