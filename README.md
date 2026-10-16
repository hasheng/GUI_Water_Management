# Quarry Water Management

Desktop app (C# / .NET 8 / Avalonia) for aggregate and quarry sites: model where water is pumped, track
Ontario Permit To Take Water (PTTW) limits, import client spreadsheets, and report on compliance.

- **Site Map** – drop a satellite image, place pits, storm ponds, sumps, wells, wash stations, pumps and outfalls, draw directional pipes, drag to rearrange.
- **Features** – table view with dewatered flags and pump capacities.
- **Permit & Takings** – permit sources with max L/min, max L/day and max days/year; daily takings.
- **Import Spreadsheet** – xlsx/csv with header-row detection, column mapping and unit conversion (m³, US/Imp gal).
- **Compliance & Reports** – exceedance checks (rate, daily volume, days/year, expiry, unpermitted source); HTML/CSV export.

Sites are saved as `.wsite` (JSON) files.

## Build
    dotnet test
    dotnet run --project src/WaterMgmt.App
    dotnet publish src/WaterMgmt.App -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
