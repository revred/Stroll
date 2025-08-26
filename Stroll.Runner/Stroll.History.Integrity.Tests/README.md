# Stroll.History.Integrity.Tests

xUnit tests that validate **Stroll.Historical** outputs (CLI-only) so **Stroll.Runner** can block any data-format or integrity regressions.

## How it works
- Launches `stroll.history` via `dotnet run` on its csproj (default) or a prebuilt exe (see `Tools/cli.config.json`).
- Uses `Integrity/integrity_dataset.csv` to sample/iterate queries.
- Verifies envelope + invariants:
  - `schema=="stroll.history.v1"`, `ok==true`, has `data` and `meta`
  - Bars: `low ≤ open,close ≤ high`, `volume ≥ 0`, timestamps monotonic if present
  - Options: `bid ≤ ask`, positive strikes if present
- Supports **smoke** (~200 rows) and **full sweep** (10,000).

## Configure (env overrides)
- `HISTORY_LAUNCH_MODE=exe|dotnet-run`
- `HISTORY_PROJECT=/abs/path/to/Stroll.Historical.csproj` (when mode=dotnet-run)
- `HISTORY_EXE=/abs/path/to/stroll.history` (when mode=exe)
- `STROLL_DATA=/abs/path/to/odte/data`
- `HISTORY_TIMEOUT_MS=60000`
- `RUN_FULL=1` to enable full sweep
- `RUN_LATENCY=1` to enable best-effort latency tests

## Run
```bash
dotnet test -c Release Stroll.History.Integrity.Tests

# full 10k sweep
RUN_FULL=1 dotnet test -c Release Stroll.History.Integrity.Tests -m:1
```
