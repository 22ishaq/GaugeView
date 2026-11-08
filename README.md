# GaugeView

**Measurement inspection for machined parts.** GaugeView checks each measured feature against its drawing
tolerance, flags anything out of spec, and warns when a feature is drifting towards a limit before it starts
making scrap. It is a C# / .NET 10 WPF desktop app, built with MVVM and unit-tested with xUnit.

[![CI](https://github.com/22ishaq/GaugeView/actions/workflows/ci.yml/badge.svg)](https://github.com/22ishaq/GaugeView/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4)
![C# WPF MVVM](https://img.shields.io/badge/C%23-WPF%20%2B%20MVVM-239120)
![xUnit](https://img.shields.io/badge/tests-63%20xUnit-blue)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

```
inspection plan (JSON) ──┐
                         ├─▶ Inspector ─▶ ToleranceChecker ─▶ PASS · WARN · FAIL · MISSING ─▶ WPF grid ─▶ CSV report
CMM / gauge CSV ─────────┘                                          │
                                                                    └─▶ Cp / Cpk per feature
```

> **Status: in progress.** The tolerance engine, file import and export, capability maths and the main WPF
> screen are done and tested. The next sprint is on the [GitHub backlog](https://github.com/22ishaq/GaugeView/issues).

## What it does

- **Checks readings against ISO 286 tolerances.** Deviations are signed, so a 12 mm H7 bore (+0.018/0) and an
  8 mm g6 spigot (−0.005/−0.014, with both limits below nominal) are entered exactly as they appear on the drawing.
- **Shows a traffic-light status for each feature.** Green when comfortably in tolerance, amber once a reading uses
  80% or more of its tolerance (adjustable with a slider), red when out of spec, and grey when not yet measured.
  A part takes the worst status of its features.
- **Calculates process capability.** Cp and Cpk per feature across every part, rated against the usual 1.33
  target, so a process that is drifting shows up before it fails parts.
- **Accepts manual entry.** Type a reading straight into the grid and the feature, the part verdict, the summary
  and the capability figures all update at once. Text that isn't a number is flagged instead of crashing the app.
- **Imports and exports files.** Load an inspection plan (JSON) and readings exported from a CMM or gauge (CSV).
  Export a per-feature report (CSV) that opens cleanly in Excel.

## How a reading is judged

| Status | Rule |
|---|---|
| **FAIL** | Outside the limits. Limits are inclusive, so a reading exactly on a limit is in spec. |
| **WARN** | In spec, but uses 80% or more of the tolerance (configurable from 50% to 100%). |
| **PASS** | In spec and below the warning threshold. |
| **MISSING** | No reading yet. The part can't pass until every feature is measured. |

*Tolerance used* is measured from the **centre of the tolerance zone**, not from nominal. That matters for
one-sided tolerances: a 12 H7 bore's zone is 12.000 to 12.018, so its centre is 12.009, and a reading of exactly
12.000 (the nominal) already uses 100% of the tolerance.

### Why `decimal`, not `double`

In binary floating point, `0.7 + 0.1` is `0.7999999999999999`, so a part measured exactly on a 0.8 mm limit
would be failed. GaugeView does all tolerance arithmetic in `decimal`, where limits are exact, and only switches
to `double` for the statistics. A unit test covers this case.

## Architecture

```
src/
  GaugeView.Core           domain model, tolerance checks, capability, CSV/JSON I/O      net10.0
  GaugeView.Presentation   view models, commands, dialog abstraction (no WPF reference)  net10.0
  GaugeView.Wpf            XAML views, converters, Windows dialogs                       net10.0-windows
tests/
  GaugeView.Core.Tests           tolerances, checker, inspector, capability, file formats
  GaugeView.Presentation.Tests   view model behaviour, commands, editing, export
```

- **MVVM with a strict split.** The view models live in their own project with no reference to WPF. Everything
  except the XAML is unit-tested, and the tests run on Linux in CI as well as on Windows.
- **No logic in code-behind.** `MainWindow.xaml.cs` only calls `InitializeComponent()`. `App.xaml.cs` is the
  composition root that gives the view model real Windows dialogs; the tests use a fake `IDialogService`.
- **Warnings are errors** across the solution, with nullable reference types on. Package versions are managed
  centrally in `Directory.Packages.props`.

## Getting started

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The app runs on Windows; the libraries and
tests build and run on any OS.

```bash
git clone https://github.com/22ishaq/GaugeView.git
cd GaugeView
dotnet run --project src/GaugeView.Wpf   # Windows only: opens with the sample data loaded
dotnet test                              # any OS: runs the 63 tests
```

CI builds and tests every push on Ubuntu and Windows, then publishes the Windows app as a downloadable artifact.

## File formats

**Inspection plan** (`samples/bracket-plan.json`). Upper and lower deviations are signed:

```json
{
  "partNumber": "BRK-1042",
  "description": "Sensor mounting bracket, 6082-T6 aluminium",
  "unit": "mm",
  "features": [
    { "name": "Bore Ø12 H7",  "nominal": 12.000, "upper": 0.018,  "lower": 0.000 },
    { "name": "Spigot Ø8 g6", "nominal": 8.000,  "upper": -0.005, "lower": -0.014 }
  ]
}
```

**Measurements** (`samples/bracket-measurements.csv`). One reading per row, with columns in any order and `#` for
comments. Numbers always use a full stop, whatever the PC's regional settings:

```csv
serial,feature,value
BRK-0001,Bore Ø12 H7,12.008
BRK-0001,Spigot Ø8 g6,7.991
```

If a part is measured twice, the later reading counts.

## Sample data

The sample is twelve brackets with six features each. With the default 80% warning threshold they come out as
**5 pass, 3 warn, 3 fail and 1 incomplete**. The mix covers every status: readings near a limit and exactly on one
(amber), three features outside their limits by 20 µm, 1 µm and 1 µm, and one part still waiting for its thickness
reading. A test checks these numbers, so this README can't drift from the code.

## Roadmap

Work is planned in short sprints on the [GitHub backlog](https://github.com/22ishaq/GaugeView/issues), with each
sprint as a [milestone](https://github.com/22ishaq/GaugeView/milestones). Next up: screenshots, an SPC run chart
per feature, a gauge R&R study, inspection history in SQLite, and an installer.

## License

[MIT](LICENSE)
