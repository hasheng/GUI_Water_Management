# GUI_Water_Management — patch series

All patches verified: `git am` applies cleanly to upstream `master` (bb5bd32)
and the full test suite passes 22/22 afterwards.

## Which file to use

| File | Use when |
|---|---|
| `gwm-all-phases-0001-0010.patch` | Fresh `master` with nothing applied — contains all 10 commits |
| `gwm-phase1-3.patch` | You already applied the Phase 0 patch (commits bc06934..246d4fc present) |
| `gwm-phase0-correctness.patch` / `gwm-phase0/` | Phase 0 alone (from the earlier delivery) |
| `gwm-all-phases-source.zip` | No git needed — full source with all 10 commits pre-applied; unzip, `cd GUI_Water_Management`, `dotnet run` |
| `gwm-phase123/` | Individual numbered patches for Phases 1–3, for cherry-picking |

## How to apply

Important: the solution file has CRLF line endings, so pass `--keep-cr`
(without it the CLI commit's .sln hunk will be rejected):

    git checkout master
    git am --keep-cr gwm-all-phases-0001-0010.patch     # or gwm-phase1-3.patch

Then verify:

    dotnet test

## The 10 commits

Phase 0 — correctness
 1. Fix spreadsheet import: Hours columns and fr-CA decimal numbers
 2. Add permit expiry notices (90/30 days, expired) to compliance and reports
 3. Add SchemaVersion to .wsite files with forward migration

Phase 1 — reporting
 4. Add monthly usage matrix and missing-day gap detection
 5. Add MECP-style annual report export (HTML/CSV) with 50k L/day flag
 6. Add import sanity checks against permit limits

Phase 2 — model
 7. Support multiple PTTWs per site (schema v2)
 8. Permit year start month for max-days-per-year limits

Phase 3 — operations
 9. Add import undo and per-source limit gauges
 10. Add water CLI for headless checks, reports and imports

## Notes

- v2 files are not readable by pre-v2 builds of the app (they ignore the
  Permits property). Apply the whole series before opening saved projects.
