# Guardrail EHS

<img src="tools/icon-preview.png" width="96" align="right" alt="Guardrail icon" />

**Free, open-source, self-hosted environmental, health & safety (EHS) and fleet safety software.**
No subscriptions, no per-employee pricing, no contracts — run it on hardware you own.

> **Status: preview (v0.6.0).** Windows desktop app. Every safety module and the full Fleet Hub work today and save to your PC;
> the rest of the product is in place as "coming soon" pages. There is no server yet.

## Working now

| Module | What you can do |
| --- | --- |
| **Incidents** | Report injuries and illnesses, environmental releases, property damage, motor vehicle incidents, operational liabilities and near misses. OSHA classification (first aid, medical treatment, restricted / transfer, days away, fatality), injury type, body part, days away and restricted, DOT recordable, root cause. Recordable, DART and lost-time cases are worked out for you. **Create action** opens a linked corrective action. |
| **Audits & Inspections** | Checklists (three starters included: monthly workplace inspection, forklift pre-use, fire extinguisher monthly; add your own). Answer each item Pass / Fail / N/A with notes, save for later or complete. Scores ignore N/A. **Create actions** makes one action per failed item. |
| **Risk Assessment** | Task and hazard rated on a 5 × 5 likelihood × severity matrix before and after controls (1–4 Low, 5–9 Medium, 10–14 High, 15–25 Extreme), with a live heat map and review dates. |
| **Management of Change** | Process, equipment, material, people, procedure and facility changes: Requested → Under review → Approved → Implemented → Closed (or Rejected). Approval needs a risk assessment; temporary changes are flagged when their end date passes. |
| **Reports** | OSHA 300 log, OSHA 300A summary, incident rates by month, incidents by type and location, overdue actions, inspection findings, training expiring, compliance due, LOTO inspections due, products missing an SDS, open claims. Preview any report and export it to CSV. |
| **Actions** | Create, edit, close and delete corrective / preventive actions with owner, location, due date and priority. Overdue is detected automatically. Filter, search, export to CSV. |
| **Observations** | Report unsafe conditions, unsafe behaviors and good catches (reporter optional, for anonymous reports). **Create action** turns any observation into a linked, pre-filled action. Filter, search, export to CSV. |
| **Safety Training** | Log completed training per person with optional expiry. Status is worked out for you: Current, Expiring soon (30 days), Expired or No expiry. Filter, search, export to CSV. |
| **Regulatory Compliance** | EPA, OSHA, DOT, state and local requirements: permits, licenses, reports, inspections and plans, with reference number, owner and next due date. Status is worked out for you: Upcoming, Due soon (30 days), Overdue or Completed. **Mark done** rolls monthly, quarterly and yearly items forward to the next due date (one-time items close). Filter, search, export to CSV. |
| **Claims Register** | Workers' comp, auto, general liability and property claims: claimant, date of loss, carrier and claim number, adjuster, related incident, reserve and amount paid. Total incurred (paid + open reserve), days open and closed-this-year are worked out for you. Filter, search, export to CSV. |
| **Library** | Policies, procedures, programs, forms and training material. Guardrail keeps its own copy of each file (so moving the original doesn't break it), with version, owner and next review date. **Open** opens the file; review reminders 30 days ahead. Filter, search, export to CSV. |
| **Monthly Data** | Hours worked, average headcount and fleet miles for each location and month: the exposure numbers OSHA incident rates are calculated from. Rolling 12-month totals; duplicate months are caught. Filter by year, search, export to CSV. |
| **Behavioral Observations** | Behavior-based safety (BBS) cards: task, focus area (PPE, body position, tools, housekeeping, procedures, driving), safe and at-risk counts and whether feedback was given. % safe is worked out per card and for the last 30 days. **Create action** turns at-risk findings into a linked action. |
| **Work Permits** | Hot work, confined space, working at height, excavation, electrical and line-break permits: who it's issued to, where, valid dates and precautions. Permits past their end date that weren't closed show as **Expired**; **Close out** finishes them. |
| **Lockout / Tagout** | Equipment-specific energy control procedures: energy sources, isolation points, an optional procedure document or photo, and the annual periodic inspection OSHA requires (1910.147). Statuses: Current, Due soon, Overdue, Not inspected. **Inspected today** records an inspection in one click. |
| **Safety Data Sheets** | Chemical inventory: product, manufacturer, signal word (Danger / Warning), hazards, location, quantity and the SDS file itself. **Open** shows the sheet; products without one are flagged **Missing SDS**. |
| **Fleet Hub** | **Overview** (everything overdue or due in 30 days across the fleet), **Driver Qualification** (license, medical certificate, annual MVR review, road test, DQ file document; Qualified / Expiring soon / Expired / Incomplete), **Accident Register** (49 CFR 390.15: injuries, fatalities, tow-away, hazmat, DOT recordable, preventability, dashcam video), **Vehicle Management** (registration, annual DOT inspection, odometer, in / out of service), **DVIR Inspections** (pre- and post-trip, defects put the unit out of service until a repair is certified), **Maintenance** (PM schedules that repeat, completing an annual DOT inspection updates the vehicle), **Hours of Service** (log reviews with 11-hour and 14-hour violations flagged), **DOT Compliance Audit** (a live mock audit across all of the above with readiness %, gaps and an exportable report) and **Fleet Reporting** (driver application status, DQ file status, vehicle maintenance and renewal status, DOT accident register, DVIR defects, HOS violations, fleet roster). |
| **Dashboard** | Live, by period (this year, last 12 months, last year) and location: days since the last recordable injury and vehicle incident, incidents by type, actions open / closed / overdue, OSHA classification breakdown, recordables by location, and TRIR, DART, LTIR, lost-time and restricted days rates and the accident frequency rate (from Monthly Data hours and miles). |
| Appearance | Light / dark mode (header toggle, or Settings → Appearance → "Match Windows"). |

Records are saved as JSON in `%AppData%\Guardrail\data\` (Settings → Your data → **Open folder**);
files attached to Library documents, LOTO procedures and Safety Data Sheets are copied into `data\library\`, `data\loto\` and `data\sds\`.
Each save writes a temp file first and keeps a `.bak` copy, so a crash can't corrupt your records.

## Coming soon

**Crash Review AI**: drop in accident video and AI determines who was at fault, fully automatically. Until then,
the Accident Register keeps the video with each accident.

## Download

Windows 10/11 (x64). Get it from the [latest release](../../releases/latest):

- **`GuardrailEHS-<version>-Setup.exe`**: installer with Start Menu shortcut, optional desktop shortcut and an
  uninstaller. Installs just for you by default (no administrator prompt); the first page lets you install for all users.
- **`GuardrailEHS-<version>-win-x64.zip`**: portable. Unzip anywhere and run `Guardrail.exe`.

Or install it from the **[Bytehawk Studio Launcher](https://github.com/adamglassner/bytehawklauncher)**, which
downloads the newest release, installs it and keeps it up to date automatically.

Neither needs .NET installed. Uninstalling keeps your records in `%AppData%\Guardrail\`.

## Releasing a new version

Change `<Version>` in `src/Guardrail.Desktop/Guardrail.Desktop.csproj` (for example to `0.2.1`) and push to `main`.
GitHub Actions ([build-release.yml](.github/workflows/build-release.yml)) then, on a Windows machine:

1. builds the single-file exe, the portable zip and the installer,
2. runs the self-test (`Guardrail.exe --self-test result.txt`): every page and Fleet Hub section in light and dark,
   the add/edit dialog, and a save/reload/update/delete round trip of the data store in a temp folder,
3. silently installs the Setup .exe to a custom folder (the way the launcher does), starts it, and uninstalls it,
4. takes light/dark screenshots (attached to the run as artifacts),
5. publishes GitHub Release `v<version>` with the Setup .exe and the zip.

Installed copies pick up the new version through the Bytehawk Studio Launcher. Pushes that don't change the
version, and pull requests, run steps 1 to 4 only.

## Building it yourself

Requirements: Windows 10/11, the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), and
[Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer (`winget install JRSoftware.InnoSetup`).

- Run from source: double-click `run.bat`, run `dotnet run --project src\Guardrail.Desktop`, or open `Guardrail.sln`
  in Visual Studio 2022 and press F5.
- Build everything: `.\build.ps1` makes `dist\Guardrail.exe`, `dist\GuardrailEHS-<version>-win-x64.zip` and
  `dist\GuardrailEHS-<version>-Setup.exe`. Use `-SkipInstaller` to skip Inno Setup.

Command-line options: `--page <module>` opens a page at startup (for example `--page actions`), and
`--self-test <file>` runs the checks above and exits with the number of failures.

## Project layout

```
src/Guardrail.Desktop/
  App.xaml(.cs)          app startup + light/dark theme switching
  MainWindow.xaml(.cs)   header, module toolbar, page host, toast
  Assets/                app icon (.ico + .png)
  Themes/                Light.xaml, Dark.xaml (colors), Controls.xaml (styles)
  Data/                  record types and the local JSON store
  Models/                ModuleCatalog.cs — every module, its planned features and columns
  Views/                 module pages, FormDialog (shared add/edit dialog), Editors (per-module forms)
  Services/              settings, CSV export, Windows theme detection, toast helper, self-test
installer/GuardrailEHS.iss  Inno Setup script (+ wizard images)
build.ps1                exe + portable zip + installer
.github/workflows/       CI: build, self-test, installer test, screenshots, release
tools/make_icon.py       regenerates the icon (Python + Pillow)
tools/make_wizard_images.py  regenerates the installer wizard images
```

To add a coming-soon module, add an entry to `Models/ModuleCatalog.cs`.
To build a module for real, copy one of the working ones (Actions is the simplest pattern):
a record class in `Data/Records.cs`, a store in `Data/DataStore.cs`, a form in `Views/Editors.cs`
and a page in `Views/`.

## Roadmap

1. Incidents (with OSHA 300/300A/301) and Monthly Data, which make the dashboard rates real
2. Audits & Inspections with checklists and offline capture
3. Guardrail Server (self-hosted) with sign-in and sync
4. Fleet Hub: driver qualification files, DVIR, maintenance
5. Crash Review AI (local models — video never leaves your server)

## License

MIT — see [LICENSE](LICENSE).
