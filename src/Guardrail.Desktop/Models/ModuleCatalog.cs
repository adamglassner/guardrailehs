namespace Guardrail.Models;

/// <summary>
/// Every module in Guardrail EHS. Actions, Observations and Safety Training have real pages
/// (see MainWindow.Show); everything else uses the generic ComingSoonView built from this data.
/// Add a module here and it appears in the toolbar automatically.
/// </summary>
public static class ModuleCatalog
{
    public static readonly ModuleInfo Dashboard = new()
    {
        Key = "dashboard", Title = "Dashboard", ToolbarLabel = "DASHBOARD", Glyph = "",
        Tagline = "Safety performance at a glance.",
    };

    public static readonly IReadOnlyList<ModuleInfo> Toolbar =
    [
        Dashboard,
        new()
        {
            Key = "incidents", Title = "Incidents", ToolbarLabel = "INCIDENTS", Glyph = "",
            Tagline = "Report, investigate and close out workplace events.",
            Description = "Capture injuries, illnesses, environmental releases, property damage, vehicle incidents, operational liabilities and near misses from any device. Guided investigations, root cause analysis and automatic OSHA and DOT recordkeeping.",
            PrimaryAction = "Report incident",
            Kpis = ["Open incidents", "Recordable (YTD)", "Near misses (YTD)", "Investigations due"],
            Columns = ["Incident #", "Date", "Type", "Location", "Reported by", "Status"],
            Features =
            [
                "Guided first-response wizard (what, who, where, when)",
                "Injury / illness, environmental, property, vehicle, liability and near miss types",
                "Root cause investigations with corrective actions",
                "Automatic OSHA 300, 300A and 301 logs",
                "DOT recordable crash reporting",
                "Photo, video and document attachments",
                "Link claims and actions to each incident",
                "Offline capture on tablets and phones with auto-sync",
            ],
        },
        new()
        {
            Key = "actions", Title = "Actions", ToolbarLabel = "ACTIONS", Glyph = "",
            Tagline = "Corrective and preventive actions, assigned and tracked to done.",
            Description = "Every finding from incidents, audits, observations and risk assessments becomes a trackable action with an owner, a due date and reminders.",
            PrimaryAction = "Create action",
            Kpis = ["Open", "Closed (30 days)", "Overdue", "Due this week"],
            Columns = ["Action #", "Title", "Source", "Owner", "Due", "Status"],
            Features = ["Assign, reassign and escalate", "Email and desktop reminders", "Overdue escalation rules", "Evidence of completion", "Link to any record in the system"],
        },
        new()
        {
            Key = "risk", Title = "Risk Assessment", ToolbarLabel = "RISK ASSESSMENT", Glyph = "",
            Tagline = "Identify hazards and rate risk before work starts.",
            Description = "Job hazard analyses (JHA/JSA), risk matrices and control plans with review cycles.",
            PrimaryAction = "New assessment",
            Kpis = ["Assessments", "High risk", "Reviews due", "Controls pending"],
            Columns = ["Assessment", "Task / area", "Risk level", "Owner", "Review date", "Status"],
            Features = ["Configurable 5x5 risk matrix", "JHA / JSA templates", "Hierarchy of controls", "Residual risk scoring", "Periodic review reminders"],
        },
        new()
        {
            Key = "monthly", Title = "Monthly Data", ToolbarLabel = "MONTHLY DATA", Glyph = "",
            Tagline = "Hours worked, headcount and miles driven for accurate rates.",
            Description = "Enter or import monthly exposure data per location so incident rates (TRIR, DART, LTIR) calculate correctly.",
            PrimaryAction = "Enter month",
            Columns = ["Month", "Location", "Hours worked", "Employees", "Miles driven", "Entered by"],
            Features = ["Per-location monthly hours and headcount", "Fleet miles for accident frequency rate", "CSV / Excel import", "Lock months after close"],
        },
        new()
        {
            Key = "reports", Title = "Reports", ToolbarLabel = "REPORTS", Glyph = "",
            Tagline = "Run, schedule and export safety and compliance reports.",
            Description = "A report library with filters by corporate, region, district and location. Export to Excel or PDF and schedule reports to land in inboxes automatically.",
            PrimaryAction = "Run report",
            Columns = ["Report", "Category", "Last run", "Schedule", "Format"],
            Features =
            [
                "Driver application status report",
                "Driver qualification (DQ) file status report",
                "DOT compliance safety audits",
                "DOT commercial motor vehicle incident report",
                "Vehicle maintenance renewal status report",
                "OSHA 300 / 300A / 301",
                "Incidents by type, location and injury type",
                "Scheduled email delivery, Excel and PDF export",
            ],
        },
        new()
        {
            Key = "library", Title = "Library", ToolbarLabel = "LIBRARY", Glyph = "",
            Tagline = "Policies, procedures and programs in one controlled place.",
            Description = "Version-controlled document library with read-and-acknowledge tracking.",
            PrimaryAction = "Upload document",
            Columns = ["Document", "Category", "Version", "Owner", "Review due"],
            Features = ["Version history", "Read and acknowledge", "Review reminders", "Full-text search"],
        },
        new()
        {
            Key = "claims", Title = "Claims Register", ToolbarLabel = "CLAIMS REGISTER", Glyph = "",
            Tagline = "Workers' comp, auto and liability claims tied to incidents.",
            Description = "Track claim status, reserves, payments and adjuster notes, linked back to the original incident.",
            PrimaryAction = "Add claim",
            Kpis = ["Open claims", "Closed (YTD)", "Total incurred", "Avg days open"],
            Columns = ["Claim #", "Type", "Incident", "Claimant", "Opened", "Status"],
            Features = ["Workers' comp, auto, general liability", "Reserves and payments", "Adjuster and carrier contacts", "Link to incidents"],
        },
        new()
        {
            Key = "bbs", Title = "Behavioral Observations", ToolbarLabel = "BEHAVIORAL OBSERVATIONS", Glyph = "",
            Tagline = "Behavior-based safety (BBS) observation cards.",
            Description = "Peer-to-peer safe / at-risk behavior observations with trend analysis.",
            PrimaryAction = "New observation",
            Columns = ["Date", "Observer", "Area", "Safe", "At-risk", "Follow-up"],
            Features = ["Custom observation cards", "Safe vs at-risk trending", "Anonymous mode", "Coaching follow-ups"],
        },
        new()
        {
            Key = "observations", Title = "Observations", ToolbarLabel = "OBSERVATIONS", Glyph = "",
            Tagline = "Hazard and good-catch reporting from anyone.",
            Description = "Quick hazard reports and positive observations from the field, with photos and location.",
            PrimaryAction = "New observation",
            Columns = ["Date", "Type", "Location", "Reported by", "Status"],
            Features = ["30-second mobile reporting", "Photo capture", "Convert to action", "Recognition for good catches"],
        },
        new()
        {
            Key = "moc", Title = "Management of Change", ToolbarLabel = "MANAGEMENT OF CHANGE", Glyph = "",
            Tagline = "Review safety impact before changes go live.",
            Description = "Structured MOC workflow for process, equipment, people and organizational changes.",
            PrimaryAction = "Request change",
            Columns = ["MOC #", "Change", "Type", "Requester", "Stage", "Status"],
            Features = ["Approval workflow", "Pre-startup safety review (PSSR)", "Temporary vs permanent changes", "Linked risk assessments"],
        },
        new()
        {
            Key = "permits", Title = "Work Permits", ToolbarLabel = "WORK PERMITS", Glyph = "",
            Tagline = "Hot work, confined space, working at height and more.",
            Description = "Digital permit-to-work with approvals, isolations and close-out.",
            PrimaryAction = "Issue permit",
            Columns = ["Permit #", "Type", "Location", "Issued to", "Valid until", "Status"],
            Features = ["Hot work, confined space, height, excavation", "Approvals and sign-offs", "Gas test records", "Permit close-out"],
        },
        new()
        {
            Key = "loto", Title = "Lockout / Tagout", ToolbarLabel = "LOTO", Glyph = "",
            Tagline = "Equipment-specific energy control procedures.",
            Description = "LOTO procedures with energy sources, isolation points, photos and annual periodic inspections (OSHA 1910.147).",
            PrimaryAction = "New procedure",
            Columns = ["Equipment", "Procedure #", "Energy sources", "Last inspection", "Status"],
            Features = ["Equipment-specific procedures", "Isolation point photos", "Printable placards", "Annual periodic inspections"],
        },
        new()
        {
            Key = "fleet", Title = "Fleet Hub", ToolbarLabel = "FLEET HUB", Glyph = "",
            Tagline = "Drivers, vehicles, inspections and DOT/FMCSA compliance.",
        },
        new()
        {
            Key = "sds", Title = "Safety Data Sheets", ToolbarLabel = "SDS", Glyph = "",
            Tagline = "Chemical inventory and SDS library.",
            Description = "Searchable SDS library with chemical inventory by location and GHS labels.",
            PrimaryAction = "Add SDS",
            Columns = ["Product", "Manufacturer", "Location", "Revision date", "Hazards"],
            Features = ["SDS search and upload", "Chemical inventory by location", "GHS label printing", "Revision tracking"],
        },
        new()
        {
            Key = "regulatory", Title = "Regulatory Compliance", ToolbarLabel = "REGULATORY COMPLIANCE", Glyph = "",
            Tagline = "EPA, OSHA and DOT requirements, permits and licenses.",
            Description = "Compliance calendar for permits, licenses, reports and recurring regulatory tasks.",
            PrimaryAction = "Add requirement",
            Kpis = ["Requirements", "Due in 30 days", "Overdue", "Permits / licenses"],
            Columns = ["Requirement", "Agency", "Location", "Due", "Owner", "Status"],
            Features = ["EPA, OSHA and DOT obligations", "Permit and license tracking", "Renewal reminders", "Compliance calendar"],
        },
        new()
        {
            Key = "dvir", Title = "Driver Vehicle Inspection Report", ToolbarLabel = "DRIVER VEHICLE INSPECTION", Glyph = "",
            Tagline = "Pre-trip and post-trip DVIRs.",
        },
        new()
        {
            Key = "audits", Title = "Audits & Inspections", ToolbarLabel = "AUDITS & INSPECTIONS", Glyph = "",
            Tagline = "Checklists in the field, even without signal.",
            Description = "Build any checklist, run it on desktop or tablet, capture photos and signatures offline, and sync when you reconnect.",
            PrimaryAction = "Start inspection",
            Kpis = ["Completed (30 days)", "Scheduled", "Findings open", "Avg score"],
            Columns = ["Inspection", "Template", "Location", "Inspector", "Score", "Status"],
            Features = ["Checklist builder", "Offline capture with auto-sync", "Photos and signatures", "Scoring and findings", "Scheduled recurring inspections", "Findings become actions"],
        },
        new()
        {
            Key = "training", Title = "Safety Training", ToolbarLabel = "SAFETY TRAINING", Glyph = "",
            Tagline = "Training matrix, records and expirations.",
            Description = "Track who needs what training, when it expires, and keep certificates on file.",
            PrimaryAction = "Record training",
            Kpis = ["Employees", "Trainings due", "Expired", "Completed (30 days)"],
            Columns = ["Employee", "Course", "Completed", "Expires", "Status"],
            Features = ["Training matrix by role", "Certificates on file", "Expiration alerts", "Sign-in sheets"],
        },
        new()
        {
            Key = "ai", Title = "Crash Review AI", ToolbarLabel = "CRASH REVIEW AI", Glyph = "", IsNew = true,
            Tagline = "AI watches the video and determines who was at fault.",
        },
    ];

    /// <summary>Sections inside Fleet Hub (modeled on the Fleet360 feature set).</summary>
    public static readonly IReadOnlyList<ModuleInfo> FleetSections =
    [
        new()
        {
            Key = "fleet-overview", Title = "Overview", Glyph = "",
            Tagline = "Your whole fleet's safety and compliance picture.",
            Description = "A single dashboard for drivers, vehicles, inspections, maintenance and DOT readiness.",
            PrimaryAction = "Add vehicle",
            Kpis = ["Active drivers", "Vehicles", "Docs expiring (30 days)", "Open DVIR defects"],
            Columns = ["Unit #", "Type", "Assigned driver", "Last inspection", "Next service", "Status"],
            Features = ["Fleet-wide KPIs", "Expiration and overdue alerts", "Per-location fleet views", "FMCSA-ready records"],
        },
        new()
        {
            Key = "fleet-dq", Title = "Driver Qualification", Glyph = "",
            Tagline = "Keep every driver qualification file complete and current.",
            Description = "Centralized DQ files with license, medical card and certification tracking. Alerts for missing or expired documents, plus digital upload, review and approval.",
            PrimaryAction = "Add driver",
            Kpis = ["Drivers on file", "Complete DQ files", "Expiring soon", "Missing documents"],
            Columns = ["Driver", "CDL class", "CDL expires", "Medical card expires", "Annual MVR", "Status"],
            Features =
            [
                "Driver applications and onboarding",
                "License, medical card and certification tracking",
                "Automated notices for missing or expired documents",
                "Digital upload, review and approval",
                "Annual MVR review and certificate of violations",
                "Driver application status report",
            ],
        },
        new()
        {
            Key = "fleet-accidents", Title = "Accident Register", Glyph = "",
            Tagline = "Vehicle accidents from first report to resolution.",
            Description = "Log fleet accidents with details, photos and witness statements. Preventability analysis using FMCSA-aligned criteria, corrective actions and driver coaching.",
            PrimaryAction = "Log accident",
            Kpis = ["Accidents (YTD)", "Preventable", "Non-preventable", "Pending review"],
            Columns = ["Report #", "Date", "Driver", "Unit", "Location", "Preventability", "Status"],
            Features =
            [
                "FMCSA-style vehicle accident register",
                "Photos, diagrams and witness statements",
                "Preventability determination workflow",
                "Automatic AI fault analysis from video (Crash Review AI)",
                "Corrective actions and driver coaching",
                "DOT recordable crash flagging",
            ],
        },
        new()
        {
            Key = "fleet-vehicles", Title = "Vehicle Management", Glyph = "",
            Tagline = "Every unit's inspections, service history, permits and violations.",
            Description = "Vehicle records with inspections, maintenance schedules, service history, permits and violations tracked per unit.",
            PrimaryAction = "Add vehicle",
            Kpis = ["Vehicles", "Permits expiring", "Open violations", "Out of service"],
            Columns = ["Unit #", "Year / make / model", "VIN", "Plate", "Registration expires", "Status"],
            Features = ["Vehicle profiles and documents", "Registration and permit tracking", "Roadside violations by unit", "Annual DOT inspection records"],
        },
        new()
        {
            Key = "fleet-dvir", Title = "DVIR Inspections", Glyph = "",
            Tagline = "Pre-trip and post-trip driver vehicle inspection reports.",
            Description = "Digital DVIR checklists drivers complete on a tablet or phone, even offline. Defects route to maintenance and repairs are certified before the vehicle goes back out.",
            PrimaryAction = "New DVIR",
            Kpis = ["Inspections today", "Defects reported", "Awaiting repair", "Certified repaired"],
            Columns = ["DVIR #", "Date", "Driver", "Unit", "Type", "Defects", "Status"],
            Features = ["Pre-trip / post-trip checklists", "Defect photos", "Offline capture with auto-sync", "Mechanic repair certification", "Driver review signature"],
        },
        new()
        {
            Key = "fleet-maintenance", Title = "Maintenance", Glyph = "",
            Tagline = "Preventive maintenance schedules and service records.",
            Description = "Maintenance scheduling with automated reminders, service and repair logging, and maintenance history ready for audits.",
            PrimaryAction = "Schedule service",
            Kpis = ["Due this week", "Overdue", "Completed (30 days)", "Avg downtime"],
            Columns = ["Work order", "Unit", "Service", "Due", "Vendor", "Status"],
            Features = ["PM schedules by miles, hours or date", "Automated reminders", "Service and repair log", "Vehicle maintenance renewal status report"],
        },
        new()
        {
            Key = "fleet-hos", Title = "Hours of Service", Glyph = "",
            Tagline = "HOS logs and violations for DOT recordkeeping.",
            Description = "Import ELD logs, review hours-of-service and flag violations for coaching.",
            PrimaryAction = "Import logs",
            Kpis = ["Drivers on duty", "HOS violations", "Logs to review", "ELD devices"],
            Columns = ["Driver", "Date", "Driving", "On duty", "Violations", "Status"],
            Features = ["ELD log import", "Violation flagging", "Supporting documents", "Retention for audits"],
        },
        new()
        {
            Key = "fleet-dot", Title = "DOT Compliance Audit", Glyph = "",
            Tagline = "Find gaps before the auditor does.",
            Description = "Review driver files, logs, inspections and maintenance history in one place, flag compliance gaps, and generate DOT-ready reports with supporting documentation.",
            PrimaryAction = "Start mock audit",
            Kpis = ["Audit readiness", "Gaps flagged", "Files reviewed", "Last mock audit"],
            Columns = ["Area", "Requirement", "Records checked", "Gaps", "Status"],
            Features = ["Mock DOT audits", "Gap flagging", "DOT-ready report packages", "Supporting documentation bundles"],
        },
        new()
        {
            Key = "fleet-reports", Title = "Fleet Reporting", Glyph = "",
            Tagline = "Trends and KPIs for incidents, maintenance and compliance.",
            Description = "Monitor safety and performance indicators, track trends to spot risk, and export summary reports for leadership or audits.",
            PrimaryAction = "Run report",
            Columns = ["Report", "Category", "Last run", "Schedule"],
            Features = ["Incident and maintenance trends", "Driver scorecards", "Exportable summaries", "Scheduled report sharing"],
        },
    ];

    public static ModuleInfo? Find(string key) =>
        Toolbar.FirstOrDefault(m => m.Key == key) ?? FleetSections.FirstOrDefault(m => m.Key == key);
}
