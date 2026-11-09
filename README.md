# Guardrail EHS

<img src="tools/icon-preview.png" width="96" align="right" alt="Guardrail icon" />

**Free, open-source, self-hosted environmental, health & safety (EHS) and fleet safety software.**
No subscriptions, no per-employee pricing, no contracts — run it on hardware you own.

> **Status: preview (v0.3.0).** Windows desktop app. Six modules work today and save to your PC;
> the rest of the product is in place as "coming soon" pages. There is no server yet.

## Working now

| Module | What you can do |
| --- | --- |
| **Actions** | Create, edit, close and delete corrective / preventive actions with owner, location, due date and priority. Overdue is detected automatically. Filter, search, export to CSV. |
| **Observations** | Report unsafe conditions, unsafe behaviors and good catches (reporter optional, for anonymous reports). **Create action** turns any observation into a linked, pre-filled action. Filter, search, export to CSV. |
| **Safety Training** | Log completed training per person with optional expiry. Status is worked out for you: Current, Expiring soon (30 days), Expired or No expiry. Filter, search, export to CSV. |
| **Regulatory Compliance** | EPA, OSHA, DOT, state and local requirements: permits, licenses, reports, inspections and plans, with reference number, owner and next due date. Status is worked out for you: Upcoming, Due soon (30 days), Overdue or Completed. **Mark done** rolls monthly, quarterly and yearly items forward to the next due date (one-time items close). Filter, search, export to CSV. |
| **Claims Register** | Workers' comp, auto, general liability and property claims: claimant, date of loss, carrier and claim number, adjuster, related incident, reserve and amount paid. Total incurred (paid + open reserve), days open and closed-this-year are worked out for you. Filter, search, export to CSV. |
| **Library** | Policies, procedures, programs, forms and training material. Guardrail keeps its own copy of each file (so moving the original doesn't break it), with version, owner and next review date. **Open** opens the file; review reminders 30 days ahead. Filter, search, export to CSV. |
| Dashboard | The Actions open / closed / overdue cards show live counts and open the filtered list. Other figures are sample data. |
| Appearance | Light / dark mode (header toggle, or Settings → Appearance → "Match Windows"). |

Records are saved as JSON in `%AppData%\Guardrail\data\` (Settings → Your data → **Open folder**);
Library files are copied into `data\library\`.
Each save writes a temp file first and keeps a `.bak` copy, so a crash can't corrupt your records.

## Coming soon

Incidents, Risk Assessment, Monthly Data, Reports, Behavioral Observations,
Management of Change, Work Permits, Lockout/Tagout, Safety Data Sheets, Audits & Inspections, the full **Fleet Hub** (Driver Qualification, Accident Register, Vehicle Management,
DVIR, Maintenance, Hours of Service, DOT Compliance Audit, Fleet Reporting) and **Crash Review AI**
(drop in accident video and AI determines who was at fault, fully automatically).

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
