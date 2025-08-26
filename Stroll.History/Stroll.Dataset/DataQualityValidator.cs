using System.Globalization;

namespace Stroll.Dataset;

// Comprehensive data quality validation for financial data
public static class DataQualityValidator
{
    public static DataQualityReport ValidateBars(IReadOnlyList<IDictionary<string, object?>> bars)
    {
        var report = new DataQualityReport
        {
            DataType = "bars",
            TotalRecords = bars.Count,
            ValidatedAt = DateTime.UtcNow
        };

        if (bars.Count == 0)
        {
            report.OverallScore = 0;
            report.Issues.Add("No data records found");
            return report;
        }

        DateTime? lastTimestamp = null;
        var violations = new List<string>();
        var warnings = new List<string>();

        for (int i = 0; i < bars.Count; i++)
        {
            var bar = bars[i];
            var rowIssues = ValidateBarRow(bar, i, lastTimestamp);
            
            violations.AddRange(rowIssues.Violations);
            warnings.AddRange(rowIssues.Warnings);
            
            // Track timestamp for sequence validation
            if (bar.TryGetValue("t", out var timestampObj) && timestampObj is DateTime timestamp)
            {
                lastTimestamp = timestamp;
            }
        }

        // Calculate quality metrics
        report.CompletenessScore = CalculateCompleteness(bars);
        report.ConsistencyScore = CalculateConsistency(bars, violations.Count);
        report.AccuracyScore = CalculateAccuracy(bars, violations.Count);
        report.TimelinessScore = CalculateTimeliness(bars);
        
        report.ViolationCount = violations.Count;
        report.WarningCount = warnings.Count;
        report.Issues.AddRange(violations);
        report.Warnings.AddRange(warnings);
        
        // Overall score calculation
        report.OverallScore = (report.CompletenessScore + report.ConsistencyScore + 
                              report.AccuracyScore + report.TimelinessScore) / 4.0;

        return report;
    }

    public static DataQualityReport ValidateOptions(IReadOnlyList<IDictionary<string, object?>> options)
    {
        var report = new DataQualityReport
        {
            DataType = "options",
            TotalRecords = options.Count,
            ValidatedAt = DateTime.UtcNow
        };

        if (options.Count == 0)
        {
            report.OverallScore = 0;
            report.Issues.Add("No options data found");
            return report;
        }

        var violations = new List<string>();
        var warnings = new List<string>();

        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var rowIssues = ValidateOptionRow(option, i);
            
            violations.AddRange(rowIssues.Violations);
            warnings.AddRange(rowIssues.Warnings);
        }

        // Calculate quality metrics
        report.CompletenessScore = CalculateOptionsCompleteness(options);
        report.ConsistencyScore = CalculateConsistency(options, violations.Count);
        report.AccuracyScore = CalculateAccuracy(options, violations.Count);
        report.TimelinessScore = CalculateOptionsTimeliness(options);
        
        report.ViolationCount = violations.Count;
        report.WarningCount = warnings.Count;
        report.Issues.AddRange(violations);
        report.Warnings.AddRange(warnings);
        
        report.OverallScore = (report.CompletenessScore + report.ConsistencyScore + 
                              report.AccuracyScore + report.TimelinessScore) / 4.0;

        return report;
    }

    private static RowValidationResult ValidateBarRow(IDictionary<string, object?> bar, int rowIndex, DateTime? lastTimestamp)
    {
        var result = new RowValidationResult();

        try
        {
            // Required fields validation
            if (!bar.TryGetValue("t", out var timestampObj) || timestampObj == null)
            {
                result.Violations.Add($"Row {rowIndex}: Missing timestamp");
                return result;
            }

            if (!bar.TryGetValue("o", out var openObj) || !TryConvertToDecimal(openObj, out var open))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing open price");
                return result;
            }

            if (!bar.TryGetValue("h", out var highObj) || !TryConvertToDecimal(highObj, out var high))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing high price");
                return result;
            }

            if (!bar.TryGetValue("l", out var lowObj) || !TryConvertToDecimal(lowObj, out var low))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing low price");
                return result;
            }

            if (!bar.TryGetValue("c", out var closeObj) || !TryConvertToDecimal(closeObj, out var close))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing close price");
                return result;
            }

            if (!bar.TryGetValue("v", out var volumeObj) || !TryConvertToLong(volumeObj, out var volume))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing volume");
                return result;
            }

            // Parse timestamp
            DateTime timestamp;
            if (timestampObj is DateTime dt)
            {
                timestamp = dt;
            }
            else if (timestampObj is string timestampStr && DateTime.TryParse(timestampStr, out timestamp))
            {
                // Successfully parsed
            }
            else
            {
                result.Violations.Add($"Row {rowIndex}: Invalid timestamp format");
                return result;
            }

            // OHLC invariant validation
            if (low > Math.Min(open, close) || high < Math.Max(open, close))
            {
                result.Violations.Add($"Row {rowIndex}: OHLC invariant violation - low: {low}, high: {high}, open: {open}, close: {close}");
            }

            if (high < low)
            {
                result.Violations.Add($"Row {rowIndex}: High ({high}) cannot be less than Low ({low})");
            }

            // Volume validation
            if (volume < 0)
            {
                result.Violations.Add($"Row {rowIndex}: Negative volume ({volume})");
            }

            // Reasonable price range validation (basic sanity check)
            if (open <= 0 || high <= 0 || low <= 0 || close <= 0)
            {
                result.Violations.Add($"Row {rowIndex}: Non-positive prices detected");
            }

            if (high > low * 10) // High is more than 10x the low (potential error)
            {
                result.Warnings.Add($"Row {rowIndex}: Unusually large price range - High: {high}, Low: {low}");
            }

            // Timestamp sequence validation
            if (lastTimestamp.HasValue && timestamp <= lastTimestamp.Value)
            {
                result.Violations.Add($"Row {rowIndex}: Timestamp sequence violation - {timestamp} <= {lastTimestamp.Value}");
            }

            // Timezone validation (should be UTC)
            if (timestamp.Kind != DateTimeKind.Utc && timestamp.Kind != DateTimeKind.Unspecified)
            {
                result.Warnings.Add($"Row {rowIndex}: Timestamp is not UTC - Kind: {timestamp.Kind}");
            }
        }
        catch (Exception ex)
        {
            result.Violations.Add($"Row {rowIndex}: Exception during validation - {ex.Message}");
        }

        return result;
    }

    private static RowValidationResult ValidateOptionRow(IDictionary<string, object?> option, int rowIndex)
    {
        var result = new RowValidationResult();

        try
        {
            // Required fields validation
            if (!option.TryGetValue("symbol", out var symbolObj) || symbolObj?.ToString() == null)
            {
                result.Violations.Add($"Row {rowIndex}: Missing symbol");
            }

            if (!option.TryGetValue("expiry", out var expiryObj) || expiryObj?.ToString() == null)
            {
                result.Violations.Add($"Row {rowIndex}: Missing expiry");
            }

            if (!option.TryGetValue("right", out var rightObj) || rightObj?.ToString() == null)
            {
                result.Violations.Add($"Row {rowIndex}: Missing right (PUT/CALL)");
            }
            else
            {
                var right = rightObj.ToString()?.ToUpper();
                if (right != "PUT" && right != "CALL")
                {
                    result.Violations.Add($"Row {rowIndex}: Invalid right value '{right}', expected PUT or CALL");
                }
            }

            if (!option.TryGetValue("strike", out var strikeObj) || !TryConvertToDecimal(strikeObj, out var strike))
            {
                result.Violations.Add($"Row {rowIndex}: Invalid or missing strike price");
            }
            else if (strike <= 0)
            {
                result.Violations.Add($"Row {rowIndex}: Strike price must be positive ({strike})");
            }

            // Bid/Ask validation
            decimal bid = 0m, ask = 0m;
            bool hasBid = option.TryGetValue("bid", out var bidObj) && TryConvertToDecimal(bidObj, out bid);
            bool hasAsk = option.TryGetValue("ask", out var askObj) && TryConvertToDecimal(askObj, out ask);

            if (hasBid && hasAsk)
            {
                if (bid > ask && bid > 0 && ask > 0) // Allow zero bid/ask for illiquid options
                {
                    result.Violations.Add($"Row {rowIndex}: Bid ({bid}) > Ask ({ask})");
                }

                if (bid < 0)
                {
                    result.Violations.Add($"Row {rowIndex}: Negative bid price ({bid})");
                }

                if (ask < 0)
                {
                    result.Violations.Add($"Row {rowIndex}: Negative ask price ({ask})");
                }
            }

            // Greeks validation (if present)
            if (option.TryGetValue("delta", out var deltaObj) && TryConvertToDecimal(deltaObj, out var delta))
            {
                if (Math.Abs(delta) > 1)
                {
                    result.Warnings.Add($"Row {rowIndex}: Delta ({delta}) outside normal range [-1, 1]");
                }
            }

            if (option.TryGetValue("gamma", out var gammaObj) && TryConvertToDecimal(gammaObj, out var gamma))
            {
                if (gamma < 0)
                {
                    result.Warnings.Add($"Row {rowIndex}: Negative gamma ({gamma})");
                }
            }

            // Expiry date validation
            if (expiryObj?.ToString() != null && DateTime.TryParse(expiryObj.ToString(), out var expiryDate))
            {
                if (expiryDate < DateTime.Now.Date.AddDays(-30)) // More than 30 days in the past
                {
                    result.Warnings.Add($"Row {rowIndex}: Expiry date is significantly in the past ({expiryDate:yyyy-MM-dd})");
                }

                if (expiryDate > DateTime.Now.Date.AddYears(5)) // More than 5 years in the future
                {
                    result.Warnings.Add($"Row {rowIndex}: Expiry date is very far in the future ({expiryDate:yyyy-MM-dd})");
                }
            }
        }
        catch (Exception ex)
        {
            result.Violations.Add($"Row {rowIndex}: Exception during validation - {ex.Message}");
        }

        return result;
    }

    private static bool TryConvertToDecimal(object? value, out decimal result)
    {
        result = 0;
        
        if (value == null) return false;
        
        if (value is decimal d)
        {
            result = d;
            return true;
        }
        
        if (value is double dbl && !double.IsInfinity(dbl) && !double.IsNaN(dbl))
        {
            result = (decimal)dbl;
            return true;
        }
        
        if (value is float f && !float.IsInfinity(f) && !float.IsNaN(f))
        {
            result = (decimal)f;
            return true;
        }
        
        if (value is int i)
        {
            result = i;
            return true;
        }
        
        if (value is long l)
        {
            result = l;
            return true;
        }
        
        return decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryConvertToLong(object? value, out long result)
    {
        result = 0;
        
        if (value == null) return false;
        
        if (value is long l)
        {
            result = l;
            return true;
        }
        
        if (value is int i)
        {
            result = i;
            return true;
        }
        
        return long.TryParse(value.ToString(), out result);
    }

    private static double CalculateCompleteness(IReadOnlyList<IDictionary<string, object?>> data)
    {
        if (data.Count == 0) return 0;

        var requiredFields = new[] { "t", "o", "h", "l", "c", "v" };
        var totalFields = data.Count * requiredFields.Length;
        var missingFields = 0;

        foreach (var record in data)
        {
            foreach (var field in requiredFields)
            {
                if (!record.ContainsKey(field) || record[field] == null)
                {
                    missingFields++;
                }
            }
        }

        return Math.Max(0, 1.0 - (double)missingFields / totalFields);
    }

    private static double CalculateOptionsCompleteness(IReadOnlyList<IDictionary<string, object?>> data)
    {
        if (data.Count == 0) return 0;

        var requiredFields = new[] { "symbol", "expiry", "right", "strike" };
        var totalFields = data.Count * requiredFields.Length;
        var missingFields = 0;

        foreach (var record in data)
        {
            foreach (var field in requiredFields)
            {
                if (!record.ContainsKey(field) || record[field] == null)
                {
                    missingFields++;
                }
            }
        }

        return Math.Max(0, 1.0 - (double)missingFields / totalFields);
    }

    private static double CalculateConsistency(IReadOnlyList<IDictionary<string, object?>> data, int violations)
    {
        if (data.Count == 0) return 0;
        return Math.Max(0, 1.0 - (double)violations / data.Count);
    }

    private static double CalculateAccuracy(IReadOnlyList<IDictionary<string, object?>> data, int violations)
    {
        if (data.Count == 0) return 0;
        return Math.Max(0, 1.0 - (double)violations / data.Count / 2.0); // Accuracy is less sensitive to individual violations
    }

    private static double CalculateTimeliness(IReadOnlyList<IDictionary<string, object?>> data)
    {
        // For historical data, timeliness is primarily about data freshness and sequence integrity
        if (data.Count == 0) return 0;

        var sequenceIssues = 0;
        DateTime? lastTimestamp = null;

        foreach (var record in data)
        {
            if (record.TryGetValue("t", out var timestampObj) && timestampObj is DateTime timestamp)
            {
                if (lastTimestamp.HasValue && timestamp <= lastTimestamp.Value)
                {
                    sequenceIssues++;
                }
                lastTimestamp = timestamp;
            }
        }

        return Math.Max(0, 1.0 - (double)sequenceIssues / data.Count);
    }

    private static double CalculateOptionsTimeliness(IReadOnlyList<IDictionary<string, object?>> data)
    {
        // For options, timeliness relates to expiry date validity
        if (data.Count == 0) return 0;

        var staleOptions = 0;
        var now = DateTime.Now.Date;

        foreach (var record in data)
        {
            if (record.TryGetValue("expiry", out var expiryObj) && 
                DateTime.TryParse(expiryObj?.ToString(), out var expiry))
            {
                if (expiry < now.AddDays(-7)) // More than a week expired
                {
                    staleOptions++;
                }
            }
        }

        return Math.Max(0, 1.0 - (double)staleOptions / data.Count);
    }
}

public class DataQualityReport
{
    public string DataType { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int ViolationCount { get; set; }
    public int WarningCount { get; set; }
    public double OverallScore { get; set; }
    public double CompletenessScore { get; set; }
    public double ConsistencyScore { get; set; }
    public double AccuracyScore { get; set; }
    public double TimelinessScore { get; set; }
    public List<string> Issues { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime ValidatedAt { get; set; }

    public string GetScoreGrade()
    {
        return OverallScore switch
        {
            >= 0.95 => "A+ (Excellent)",
            >= 0.90 => "A (Very Good)",
            >= 0.85 => "B+ (Good)",
            >= 0.80 => "B (Acceptable)",
            >= 0.70 => "C (Poor)",
            _ => "F (Failed)"
        };
    }
}

public class RowValidationResult
{
    public List<string> Violations { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}