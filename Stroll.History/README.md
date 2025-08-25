# Stroll.History (CLI-only, ODTE-format pass-through)

**Goal:** Provide a discoverable, CLI-only historical data service that **preserves the exact row/field format of ODTE.Historical datasets**.  
Other components consume this via **commands + JSON/JSONL**, not via shared types or HTTP.

Projects:
- **Stroll.Historical** — CLI entrypoint
- **Stroll.Storage** — dataset catalog + storage providers (stub now; ODTE readers optional)
- **Stroll.Dataset** — JSON packager (returns **raw rows** exactly as stored)

## Commands
- `stroll.history discover`
- `stroll.history version`
- `stroll.history list-datasets`
- `stroll.history get-bars --symbol XSP --from 2024-01-02 --to 2024-01-02 --granularity 1m [--format json|jsonl]`
- `stroll.history get-options --symbol XSP --date 2024-01-05`

Exit codes: `0=ok, 64=usage, 65=data, 70=internal`
Env: `STROLL_DATA` root override (default `./data`).

**Success envelope:**
{ "schema": "stroll.history.v1", "ok": true,  "data": { ... }, "meta": { ... } }
**Error envelope:**
{ "schema": "stroll.history.v1", "ok": false, "error": { "code": "...", "message": "...", "hint": "..." } }

Bars & options rows are **raw dictionaries** from Parquet/SQLite (**no key renames**).

## Quick start
```bash
dotnet build Stroll.History.sln
dotnet run --project Stroll.Historical -- discover
dotnet run --project Stroll.Historical -- list-datasets
dotnet run --project Stroll.Historical -- get-bars --symbol XSP --from 2024-01-02 --to 2024-01-02 --granularity 1m
dotnet run --project Stroll.Historical -- get-bars --symbol XSP --from 2024-01-02 --to 2024-01-02 --granularity 1m --format jsonl
dotnet run --project Stroll.Historical -- get-options --symbol XSP --date 2024-01-05
```

## Enable real ODTE readers
```bash
dotnet add Stroll.Storage package Parquet.Net
dotnet add Stroll.Storage package Microsoft.Data.Sqlite
dotnet add Stroll.Storage package YamlDotNet
# then enable the symbol in Stroll.Storage.csproj:
# <DefineConstants>$(DefineConstants);ODTE_REAL</DefineConstants>
```
Place datasets under `./data` or set `STROLL_DATA`.

### Example data layout
```
data/
  XSP_1m.parquet
  SPX_1m.parquet
  VIX_1m.parquet
  XSP_options.sqlite
configs/
  column_hints.yml
```

Contracts are stable: CLI names, envelopes, and **raw keys**. Any breaking change bumps `schema`.
