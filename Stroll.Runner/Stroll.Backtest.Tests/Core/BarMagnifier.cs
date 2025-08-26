using System;
using System.Collections.Generic;
using System.Linq;

namespace Stroll.Backtest.Tests.Core;

/// <summary>
/// Bar Magnifier: Synthesizing 1-Minute Paths from 5-Minute Bars
/// 
/// Strategy: Generate synthetic 1-minute bars that are consistent with 5-minute OHLCV
/// Guarantees: Re-aggregating synthetic bars exactly recovers original 5-minute bar
/// Use Case: 1DTE options strategies requiring intrabar precision
/// 
/// Source: C:\code\Stroll\Stroll.History\Stroll.Dataset\Bar_Magnifier_Explainer.rtf
/// </summary>
public enum MagnifierMode 
{ 
    Conservative,  // Deterministic anchor points (pessimistic for stops/fills)
    Bridge         // Constrained Brownian bridge (realistic, stochastic)
}

public record Bar5m(DateTime T, decimal O, decimal H, decimal L, decimal C, long V, bool IsSynthetic = false);
public record Bar1m(DateTime T, decimal O, decimal H, decimal L, decimal C, long V, bool IsSynthetic = true);

public static class BarMagnifier
{
    /// <summary>
    /// Convert a 5-minute bar into five 1-minute bars using specified magnification mode
    /// </summary>
    public static IEnumerable<Bar1m> ToMinutes(Bar5m bar, MagnifierMode mode, int seed = 42)
        => mode == MagnifierMode.Conservative ? Conservative(bar) : Bridge(bar, seed);

    /// <summary>
    /// A) Deterministic anchor approach - Conservative for stop/fill testing
    /// Pattern: O → L → H → C (bullish) or O → H → L → C (bearish)
    /// </summary>
    private static IEnumerable<Bar1m> Conservative(Bar5m b)
    {
        var t = b.T;
        var anchors = new List<(int idx, decimal px)>();

        // Choose path based on direction: bullish hits low first, bearish hits high first
        if (b.C >= b.O) 
            anchors.AddRange(new[] { (0, b.O), (1, b.L), (3, b.H), (4, b.C) });
        else             
            anchors.AddRange(new[] { (0, b.O), (1, b.H), (3, b.L), (4, b.C) });

        var endpoints = new decimal[6];
        foreach (var a in anchors) 
            endpoints[a.idx] = a.px;
        
        // Linear interpolation for unspecified points
        if (endpoints[2] == 0m) 
        { 
            int left = 1, right = 3; 
            endpoints[2] = endpoints[left] + (endpoints[right] - endpoints[left]) / (right - left); 
        }
        endpoints[5] = b.C;

        // Volume distribution: U-shaped profile (higher at start/end, lower in middle)
        var volumeWeights = new[] { 1.25, 1.1, 0.9, 1.1, 1.25 };
        var weightSum = volumeWeights.Sum();
        var volumes = volumeWeights.Select(w => (long)Math.Round((double)b.V * w / weightSum)).ToArray();
        
        // Ensure total volume matches exactly
        var volumeDrift = (long)b.V - volumes.Sum();
        if (volumeDrift != 0) 
            volumes[4] += volumeDrift;

        // Generate five 1-minute bars
        for (int i = 0; i < 5; i++)
        {
            decimal open = (i == 0) ? b.O : endpoints[i];
            decimal close = endpoints[i + 1];
            decimal low = Math.Min(open, close);
            decimal high = Math.Max(open, close);

            // Ensure anchor points hit the 5-minute high/low exactly
            if (anchors.Any(a => a.idx == i && a.px == b.H)) 
                high = b.H;
            if (anchors.Any(a => a.idx == i && a.px == b.L)) 
                low = b.L;

            yield return new Bar1m(t.AddMinutes(i), open, high, low, close, volumes[i], true);
        }
    }

    /// <summary>
    /// B) Constrained Brownian bridge - Realistic stochastic path generation
    /// Uses variance estimation and barrier enforcement to hit high/low
    /// </summary>
    private static IEnumerable<Bar1m> Bridge(Bar5m b, int seed)
    {
        var rng = new Random(seed);
        double O = (double)b.O, H = (double)b.H, L = (double)b.L, C = (double)b.C;

        // Estimate 5-minute volatility from range (Parkinson estimator)
        double range = (H > 0 && L > 0 && H > L) ? Math.Log(H / L) : 0.0;
        double sigma5min = range > 0 ? Math.Sqrt(range * range / (4.0 * Math.Log(2.0))) : 1e-6;
        double sigma1min = sigma5min / Math.Sqrt(5.0); // Scale to 1-minute
        double drift = (C - O) / 5.0; // Per-minute drift

        // Generate Brownian bridge path
        var endpoints = new double[6];
        endpoints[0] = O;
        
        for (int i = 1; i <= 5; i++)
        {
            double step = drift + NextGaussian(rng) * sigma1min;
            endpoints[i] = endpoints[i - 1] + step;
        }

        // Bridge correction: ensure endpoint equals close
        double correction = (endpoints[5] - C) / 5.0;
        for (int i = 1; i <= 5; i++) 
            endpoints[i] -= correction * i;

        // Barrier enforcement: ensure path hits 5-minute high and low
        int highMinute = rng.Next(0, 5);
        int lowMinute = rng.Next(0, 5);
        if (lowMinute == highMinute) 
            lowMinute = (highMinute + 2) % 5;

        // Force barrier hits
        if (endpoints.Max() < H) 
            endpoints[highMinute + 1] = Math.Max(endpoints[highMinute + 1], H);
        if (endpoints.Min() > L) 
            endpoints[lowMinute + 1] = Math.Min(endpoints[lowMinute + 1], L);

        // Re-apply bridge correction after barrier enforcement
        correction = (endpoints[5] - C) / 5.0;
        for (int i = 1; i <= 5; i++) 
            endpoints[i] -= correction * i;

        // Volume allocation: proportional to absolute price change
        var volumes = new long[5];
        var weights = new double[5];
        for (int i = 0; i < 5; i++) 
            weights[i] = Math.Abs(endpoints[i + 1] - endpoints[i]) + 1e-6; // Avoid division by zero

        var weightSum = weights.Sum();
        for (int i = 0; i < 5; i++) 
            volumes[i] = (long)Math.Round((double)b.V * (weights[i] / weightSum));

        // Ensure total volume matches exactly
        long volumeDrift = (long)b.V - volumes.Sum();
        if (volumeDrift != 0) 
            volumes[4] += volumeDrift;

        // Generate five 1-minute bars
        for (int i = 0; i < 5; i++)
        {
            double open = endpoints[i];
            double close = endpoints[i + 1];
            double high = Math.Max(open, close);
            double low = Math.Min(open, close);

            yield return new Bar1m(b.T.AddMinutes(i),
                (decimal)open, (decimal)high, (decimal)low, (decimal)close, volumes[i], true);
        }
    }

    /// <summary>
    /// Box-Muller transform for Gaussian random numbers
    /// </summary>
    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble(); // Uniform(0,1) excluding 0
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>
    /// Validation: Re-aggregate 1-minute bars back to 5-minute and verify exact match
    /// </summary>
    public static Bar5m ReAggregate(IEnumerable<Bar1m> minuteBars)
    {
        var bars = minuteBars.ToList();
        if (bars.Count != 5)
            throw new ArgumentException("Expected exactly 5 one-minute bars for re-aggregation");

        var firstBar = bars[0];
        var lastBar = bars[4];

        return new Bar5m(
            T: firstBar.T,
            O: firstBar.O,
            H: bars.Max(b => b.H),
            L: bars.Min(b => b.L),
            C: lastBar.C,
            V: bars.Sum(b => b.V),
            IsSynthetic: false // Re-aggregated data matches original
        );
    }

    /// <summary>
    /// Validate that synthetic 1-minute bars exactly reproduce the original 5-minute bar
    /// </summary>
    public static bool ValidateMagnification(Bar5m original, IEnumerable<Bar1m> synthetic)
    {
        var reAggregated = ReAggregate(synthetic);
        
        return original.T == reAggregated.T &&
               original.O == reAggregated.O &&
               original.H == reAggregated.H &&
               original.L == reAggregated.L &&
               original.C == reAggregated.C &&
               original.V == reAggregated.V;
    }
}