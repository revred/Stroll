# Options Data QA Pack — 10,000 Rows (Synthetic)

This package contains **10,000 synthetic option datapoints** designed to exercise our ingestion, feature generation, and backtest engines across **0DTE → 365DTE** for indices, ETFs, and stocks. Use it to:
- **Discover data presence** (do we have the fields we expect for options backtests?).
- **Validate accuracy** (re-derive prices/greeks/IV, check NBBO invariants, session rules).
- **Regression-test** feature and execution pipelines (consistent output as code evolves).
- **Speed-test** moving between 1-minute and 5-minute data in SQLite buckets.

> File: `Options-QA-10000.csv` (10,000 rows, UTF‑8, commas).

## 1) Columns & Intended Checks

| Column | Purpose / Expected Check |
|---|---|
| ts | ISO‑8601 UTC minute. Verify parsing, partitioning, and session windows. |
| underlying, underlying_kind | Drives symbol routing, calendars, contract rules. |
| underlying_px | Underlying reference for greeks/IV. |
| contract | OCC option symbol for addressing unique contracts. |
| option_type | `C` or `P`. |
| style | `european` for index-style; `american` otherwise. |
| expiry, dte | Roll logic, settlement tests, T calculation in years (`dte/365`). |
| strike, multiplier | Contract economics. |
| bid, ask, last, mid, spread, spread_bps | NBBO invariants: `bid<=mid<=ask`, `spread>0`. |
| volume, open_interest | Liquidity sanity & filters. |
| iv | **Ground-truth** IV used to generate theoretical price. |
| delta, gamma, theta, vega | Greeks computed from BS using `iv`. You should be able to recompute and match within tolerance. |
| intrinsic, extrinsic | Mid‑based decomposition. |
| expected_buy_fill / expected_sell_fill | Mid ± 30% of spread, clipped to NBBO. |
| mid_in_nbbo, has_bar, has_quotes, has_trades, quality | Quick QA flags. |
| resolution (`1m`/`5m`), source (`flatfile`/`rest`) | Lets you measure speed and type‑mix resilience. |

**Tolerances for re-computation (recommended):**
- Price from BS (`theo`) vs `mid`: within **±10% of spread**.
- Greeks: absolute error tolerances — `|Δ|<=0.015`, `|Γ|<=2e-4`, `|Θ|<=0.05/365`, `|V|<=0.05`.
- IV from mid inversion: **±0.02** (2 vol points) typical; widen for deep ITM/OTM or DTE≈0.

## 2) Quickstart — Validation Harness (pseudo)

1. **Load** CSV into Pandas, Arrow, or your SQLite 1y/5y buckets.  
2. For each row, compute BS price and greeks from (`underlying_px`, `strike`, `dte/365`, `iv`, `option_type`).  
3. Check invariants:  
   - `bid<=mid<=ask`, `spread>0`  
   - `intrinsic = max(S-K,0)` for calls; `max(K-S,0)` for puts  
   - `extrinsic = max(mid - intrinsic, 0)`  
4. **Invert IV** from `mid` to re-solve `iv` and compare to provided `iv`.  
5. Simulate fills using `expected_*_fill` and ensure they lie in `[bid,ask]`.  
6. Group by `resolution` and `source` to benchmark your ETL and derived‑feature speed.

## 3) Feature Tests You Should Run

- **Moneyness**: `log_moneyness = ln(S/K)`; verify monotonic behavior of Δ with respect to moneyness and DTE.  
- **Term structure**: bucket by `dte` bands (0, 1–7, 8–30, 31–90, 91–365) and ensure spreads/vega scale sensibly.  
- **Liquidity filters**: simulate filters by `spread_bps`, `volume`, `open_interest` and confirm selection reproducibility.  
- **Execution envelopes**: confirm fills (base/stress) never break NBBO; record slippage distributions.  
- **Roll logic**: smoke‑test your roll triggers over synthetic month flips using `expiry` and `dte`.

## 4) Regression Suite (keep us honest)

- **Schema presence**: all required columns present with correct dtypes.  
- **Invariant tests**: NBBO, intrinsic/extrinsic math, non‑negative prices.  
- **Greek/IV match**: recompute and check tolerance histograms.  
- **Performance**: 10k‑row runs sub‑second in‑memory; ≤2s for SQLite scans of 1y_1min DB on a laptop‑class CPU.  
- **Determinism**: repeated runs must yield identical aggregates, histograms, and pass/fail counts.

## 5) Tips for 0DTE → 365DTE Backtests

- Treat `dte=0` with a small epsilon time value for BS (`1/365`) to avoid divide‑by‑zero while still approximating expiration dynamics.  
- Cap fills to `[bid, ask]` when quotes present; when testing bar‑only flows, cap to `[Low, High]` of the bar.  
- Use **spread‑aware** slippage and widen it under high `spread_bps` or extreme moneyness.  
- Persist your **contract discovery snapshot** per test (for real data) to ensure rerun determinism.

## 6) How this dataset helps “Do we even have the data?”

- If your ingestion expects these columns and types and passes the invariant tests on this CSV, it’s safe to proceed to live data.  
- Missing or mis‑typed fields here will immediately fail your loaders before you spend time debugging Polygon endpoints.

---

**Files in this pack**  
- `Options-QA-10000.csv` — the data (10,000 rows).  
- `Options-QA-Guide.md` — this guide.

**Pro tip:** Load this CSV into your **1‑Year / 1‑Minute** SQLite bucket as a temporary table to benchmark joins & feature generation, then drop it when you’re done.
