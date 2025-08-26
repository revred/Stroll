# Stroll.Dataset Integrity Test Guide

This test set contains **10,000 randomized data points** to sanity-check that optimizations
have **not** altered the data format or basic invariants produced by `stroll.history` (CLI-only).

## Files
- `integrity_dataset.csv` — 3 columns per row:
  1. **date** (`YYYY-MM-DD`)
  2. **instrument** — e.g. `SPY:options`, `QQQ:bars1d`. The suffix denotes which API call to run.
  3. **expected_result** — `OK` or `OK|OK-EMPTY` (chains on non-expiry dates may be empty).

## What we verify
For each row:
- **Schema**: Envelope is `{ schema: "stroll.history.v1", ok: true, data: { ... }, meta: { ... } }`.
- **Bars invariants** (when `:bars1d`): if rows exist, each row has numeric OHLC/V and `low ≤ open,close ≤ high`.
- **Options invariants** (when `:options`): if rows exist, each row has numeric `bid, ask` and `bid ≤ ask`.
- **Pass-through keys**: No key renames; keys match underlying dataset (ODTE.Historical).

## Running the checks (Python)
```bash
python3 verify_integrity.py
```

Create `verify_integrity.py` with:
```python
import csv, json, subprocess, sys

def run_cmd(args):
    p = subprocess.run(args, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    if p.returncode != 0:
        raise RuntimeError(f"cmd failed ({p.returncode}): {' '.join(args)}\n{p.stderr}")
    return json.loads(p.stdout)

def check_bars(date, symbol):
    r = run_cmd(["dotnet", "run", "--project", "Stroll.Historical", "--",
                 "get-bars", "--symbol", symbol, "--from", date, "--to", date, "--granularity", "1d"])
    assert r.get("schema") == "stroll.history.v1" and r.get("ok") is True
    bars = r["data"]["bars"]
    for b in bars:
        # Support both compact keys (t,o,h,l,c,v) and verbose (Time/Open/High/Low/Close/Volume)
        o = float(b.get("o", b.get("Open", 0)))
        h = float(b.get("h", b.get("High", 0)))
        l = float(b.get("l", b.get("Low", 0)))
        c = float(b.get("c", b.get("Close", 0)))
        v = float(b.get("v", b.get("Volume", 0)))
        assert l <= o <= h and l <= c <= h
        assert v >= 0

def check_options(date, symbol):
    r = run_cmd(["dotnet", "run", "--project", "Stroll.Historical", "--",
                 "get-options", "--symbol", symbol, "--date", date])
    assert r.get("schema") == "stroll.history.v1" and r.get("ok") is True
    chain = r["data"]["chain"]
    for row in chain:
        bid = float(row.get("bid", row.get("Bid", 0)))
        ask = float(row.get("ask", row.get("Ask", 0)))
        assert bid <= ask

def main(csv_path):
    with open(csv_path, newline="", encoding="utf-8") as f:
        rdr = csv.DictReader(f)
        for i, rec in enumerate(rdr, 1):
            date = rec["date"]
            inst = rec["instrument"]
            kind = inst.split(":")[1]
            symbol = inst.split(":")[0]
            if kind == "bars1d":
                check_bars(date, symbol)
            elif kind == "options":
                try:
                    check_options(date, symbol)
                except AssertionError:
                    print(f"{i}: FAIL {date} {inst}", file=sys.stderr)
                    sys.exit(1)
            else:
                print(f"unknown kind: {kind}", file=sys.stderr)
                sys.exit(2)
            print(f"{i}: PASS {date} {inst}")
    print("All checks passed.")

if __name__ == "__main__":
    main("integrity_dataset.csv")
```

## Running the checks (Bash + jq) for a single row
```bash
DATE=2024-05-03
SYMBOL=SPY
dotnet run --project Stroll.Historical -- get-bars --symbol "$SYMBOL" --from "$DATE" --to "$DATE" --granularity 1d | jq -e '.schema=="stroll.history.v1" and .ok==true and (.data.bars|type=="array")'

dotnet run --project Stroll.Historical -- get-options --symbol "$SYMBOL" --date "$DATE" | jq -e '.schema=="stroll.history.v1" and .ok==true'
```

## Notes
- Set `STROLL_DATA` to point to your ODTE datasets if not under `./data`.
- Empty arrays are acceptable for non-trading days and non-expiry dates (treated as OK).