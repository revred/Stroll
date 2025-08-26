# 5,000 High-Variety Integrity Data Points

This package contains two CSVs to stress **Stroll.History/Stroll.Dataset** now that
historical storage is consolidated in **SQLite** (and optionally mixed with Parquet).

- `svariety_dataset_5000.csv` — **compatible with current test harness** (only `:options` and `:bars1d`).
- `xvariety_dataset_5000_extended.csv` — adds `:bars1m` and `:bars5m` rows so you can
  turn on finer-grained tests when your harness supports multiple granularities.

Each file has 3 columns per row:
1. `date` — `YYYY-MM-DD`.
2. `instrument` — `<SYMBOL>:<kind>` where `<kind>` ∈ { `options`, `bars1d` } (main) or
   { `options`, `bars1d`, `bars1m`, `bars5m` } (extended).
3. `expected_result` — `OK` for bars, `OK|OK-EMPTY` for options (chains may be empty on non-expiry dates).

## Why this stresses the storage

- **Rapid switching** between `options` and `bars*` ensures frequent context changes for the CLI and storage layer.
- **Large symbol pool** (30+ tickers) increases the chance of crossing **multiple SQLite files/tables** or partitions.
- **Wide date span** (2016–2025) hits different ranges, indexes, and page caches.
- If your layout is per-symbol databases (e.g., `SPY_options.sqlite`, `QQQ_options.sqlite`) or per-kind shards,
  the sequence of rows will constantly force **new file opens** and **index scans**.

## Using with the current xUnit suite

The provided tests already support `:options` and `:bars1d`. To run a smoke on 200 random rows:
```bash
dotnet test Stroll.History.Integrity.Tests -c Release
```

To run a one-off spot check with the **Pretty** wrapper:
```bash
dotnet run --project Stroll.PrettyTest -- --filter FullyQualifiedName~SmokeTests
```

## Enabling the extended granularity

When you add support in the test harness for `1m` and `5m`, map kinds to CLI calls:
- `bars1m`  → `get-bars --granularity 1m`
- `bars5m`  → `get-bars --granularity 5m`

For any row kind you don’t support yet, **skip** (don’t fail). This allows you to use a single dataset file across versions.

## Notes
- Set `STROLL_DATA` to the dataset root if not under `./data`.
- Empty results are allowed for **options** on non-expiry dates and for **bars** on market holidays.