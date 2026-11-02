# Quarry Water Management

Desktop app (C# / .NET 8 / Avalonia) for aggregate and quarry sites: model where water is pumped, track
Ontario Permit To Take Water (PTTW) limits, import client spreadsheets, and report on compliance.

- **Site Map** – drop a satellite image, place pits, storm ponds, sumps, wells, wash stations, pumps and outfalls, draw directional pipes (labelled with flow), drag to rearrange.
- **Features** – table view with dewatered flags and pump capacities.
- **Permit & Takings** – multiple PTTWs per site, each with its own sources (max L/min, max L/day, max days/year), expiry countdown (warning at 90 days, critical at 30) and configurable permit-year start month; daily takings.
- **Import Spreadsheet** – xlsx/csv with header-row detection, column mapping and unit conversion (m³, US/Imp gal); handles English and fr-CA decimal formats, converts "Hours" columns to minutes, warns when imported data already exceeds permit limits, and supports session undo.
- **Mass Balance** – per-object in/out/storage change from pipe flows plus seepage/evaporation terms; flags imbalances, pumps over rated capacity or permit L/min (red outline on the map).
- **Compliance & Reports** – exceedance checks (rate, daily volume, days/year, expiry, unpermitted source); usage gauges; monthly volume matrix with missing-day gap detection; HTML/CSV export; MECP-style annual water taking report with the 50,000 L/day reporting threshold flagged.

Sites are saved as `.wsite` (JSON) files. Schema versioning migrates older saves on load (v1 single-permit files are upgraded automatically).

## CLI

    dotnet run --project src/WaterMgmt.Cli -- <command>

    water new <project.wsite>                                  create an empty project
    water check <project.wsite> [year]                         exceedances (exit 2 if any)
    water report <project.wsite> [--year N] [--out f.html]     full HTML report
    water mecp <project.wsite> [--year N] [--csv] [--out f]    annual report
    water import <sheet> <project.wsite> [--header N]          auto-mapped import
                               [--create-sources] [--dry-run]

## Build
    dotnet test
    dotnet run --project src/WaterMgmt.App
    dotnet publish src/WaterMgmt.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true

## Windows build
`WaterManagement.exe` (GUI) and `water.exe` (CLI) are self-contained single files: no .NET install needed.
The `Windows build` GitHub Action tests and publishes both on every push (download from the run's Artifacts).
