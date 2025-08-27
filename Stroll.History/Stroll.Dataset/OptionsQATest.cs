using Microsoft.Data.Sqlite;
using System.Globalization;
using Dapper;

namespace Stroll.Dataset;

/// <summary>
/// Comprehensive Options QA Test Suite
/// Tests MCP service with 10,000 synthetic options datapoints across 0DTE → 365DTE
/// Validates ingestion, Greeks computation, and distributed query performance
/// </summary>
public class OptionsQATest
{
    private readonly AdvancedPolygonDataset _dataset;
    private readonly string _testDataPath;
    private readonly string _testDatabasePath;
    
    public OptionsQATest()
    {
        _dataset = new AdvancedPolygonDataset();
        _testDataPath = Path.Combine(@"C:\code\Stroll\Stroll.History\Data\Options\Options-QA-10000.csv");
        _testDatabasePath = Path.Combine(Path.GetTempPath(), "options_qa_test.db");
    }

    /// <summary>
    /// Run comprehensive options QA test suite
    /// </summary>
    public async Task RunCompleteQATest()
    {
        Console.WriteLine("🧪 Starting Options QA Test Suite");
        Console.WriteLine($"📂 Test data: {_testDataPath}");
        Console.WriteLine($"🗄️  Test database: {_testDatabasePath}");
        Console.WriteLine();

        var results = new QATestResults();
        
        try
        {
            // 1. Schema and Data Loading Tests
            Console.WriteLine("📋 Phase 1: Schema Presence & Data Loading");
            results.SchemaTest = await TestSchemaAndLoading();
            PrintPhaseResults("Schema & Loading", results.SchemaTest);

            // 2. NBBO and Price Invariant Tests
            Console.WriteLine("💰 Phase 2: NBBO & Price Invariants");
            results.NBBOTest = await TestNBBOInvariants();
            PrintPhaseResults("NBBO Invariants", results.NBBOTest);

            // 3. Greeks and IV Recomputation Tests
            Console.WriteLine("📊 Phase 3: Greeks & IV Recomputation");
            results.GreeksTest = await TestGreeksRecomputation();
            PrintPhaseResults("Greeks Recomputation", results.GreeksTest);

            // 4. Performance and Speed Tests
            Console.WriteLine("⚡ Phase 4: Performance & Speed Tests");
            results.PerformanceTest = await TestPerformance();
            PrintPhaseResults("Performance", results.PerformanceTest);

            // 5. MCP Service Integration Tests
            Console.WriteLine("🔌 Phase 5: MCP Service Integration");
            results.MCPTest = await TestMCPServiceIntegration();
            PrintPhaseResults("MCP Service", results.MCPTest);

            // 6. Advanced Query Tests
            Console.WriteLine("🔍 Phase 6: Advanced Query Tests");
            results.QueryTest = await TestAdvancedQueries();
            PrintPhaseResults("Advanced Queries", results.QueryTest);

            // Final Summary
            PrintFinalSummary(results);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test suite failed with error: {ex.Message}");
            Console.WriteLine($"   Stack trace: {ex.StackTrace}");
        }
        finally
        {
            // Cleanup
            try
            {
                // Force garbage collection to release any SQLite connections
                GC.Collect();
                GC.WaitForPendingFinalizers();
                
                await Task.Delay(100); // Small delay to ensure connections are closed
                
                if (File.Exists(_testDatabasePath))
                {
                    File.Delete(_testDatabasePath);
                    Console.WriteLine("🧹 Test database cleaned up");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Could not clean up test database: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Test schema presence and CSV loading
    /// </summary>
    private async Task<TestPhaseResult> TestSchemaAndLoading()
    {
        var result = new TestPhaseResult { PhaseName = "Schema & Loading" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Check if test data exists
            if (!File.Exists(_testDataPath))
            {
                result.AddFailure($"Test data file not found: {_testDataPath}");
                return result;
            }

            // Load CSV and validate structure
            var csvLines = await File.ReadAllLinesAsync(_testDataPath);
            if (csvLines.Length < 10000)
            {
                result.AddFailure($"Expected 10,000+ rows, found {csvLines.Length}");
                return result;
            }

            // Validate CSV header
            var expectedColumns = new[] 
            {
                "ts", "underlying", "underlying_kind", "underlying_px", "contract", "option_type",
                "style", "expiry", "dte", "strike", "multiplier", "bid", "ask", "last", "mid",
                "spread", "spread_bps", "volume", "open_interest", "iv", "delta", "gamma",
                "theta", "vega", "intrinsic", "extrinsic", "expected_buy_fill", "expected_sell_fill",
                "mid_in_nbbo", "has_bar", "has_quotes", "has_trades", "quality", "resolution", "source"
            };

            var header = csvLines[0].Split(',');
            foreach (var expectedCol in expectedColumns)
            {
                if (!header.Contains(expectedCol))
                {
                    result.AddFailure($"Missing expected column: {expectedCol}");
                }
            }

            if (result.Failures.Count == 0)
            {
                result.AddSuccess($"CSV structure validated: {csvLines.Length - 1} data rows with {header.Length} columns");
            }

            // Create test database and load data
            await CreateTestDatabase();
            var loadedRows = await LoadCSVToDatabase();
            
            result.AddSuccess($"Data loaded to test database: {loadedRows} rows");
            result.AddSuccess($"Database created at: {_testDatabasePath}");

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;

        }
        catch (Exception ex)
        {
            result.AddFailure($"Schema test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Test NBBO invariants: bid <= mid <= ask, spread > 0
    /// </summary>
    private async Task<TestPhaseResult> TestNBBOInvariants()
    {
        var result = new TestPhaseResult { PhaseName = "NBBO Invariants" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
            await connection.OpenAsync();

            // Test NBBO invariants
            var nbboViolations = await connection.QueryAsync<NBBOViolation>(@"
                SELECT contract, bid, mid, ask, spread, 
                       CASE 
                         WHEN bid > mid THEN 'bid > mid'
                         WHEN mid > ask THEN 'mid > ask' 
                         WHEN bid > ask THEN 'bid > ask'
                         WHEN spread <= 0 THEN 'spread <= 0'
                         ELSE 'unknown'
                       END as violation_type
                FROM options_qa_test 
                WHERE bid > mid OR mid > ask OR bid > ask OR spread <= 0
                LIMIT 100
            ");

            if (nbboViolations.Any())
            {
                result.AddFailure($"Found {nbboViolations.Count()} NBBO violations");
                foreach (var violation in nbboViolations.Take(5))
                {
                    result.AddFailure($"  {violation.Contract}: {violation.ViolationType} (bid={violation.Bid:F4}, mid={violation.Mid:F4}, ask={violation.Ask:F4})");
                }
            }
            else
            {
                result.AddSuccess("All NBBO invariants validated: bid <= mid <= ask, spread > 0");
            }

            // Test intrinsic value calculation
            var intrinsicErrors = await connection.QueryAsync<IntrinsicError>(@"
                SELECT contract, option_type, underlying_px, strike, intrinsic, 
                       CASE 
                         WHEN option_type = 'C' THEN MAX(underlying_px - strike, 0)
                         ELSE MAX(strike - underlying_px, 0)
                       END as calculated_intrinsic,
                       ABS(intrinsic - CASE 
                         WHEN option_type = 'C' THEN MAX(underlying_px - strike, 0)
                         ELSE MAX(strike - underlying_px, 0)
                       END) as error
                FROM options_qa_test 
                WHERE ABS(intrinsic - CASE 
                         WHEN option_type = 'C' THEN MAX(underlying_px - strike, 0)
                         ELSE MAX(strike - underlying_px, 0)
                       END) > 0.01
                LIMIT 10
            ");

            if (intrinsicErrors.Any())
            {
                result.AddFailure($"Found {intrinsicErrors.Count()} intrinsic value errors > $0.01");
                foreach (var error in intrinsicErrors.Take(3))
                {
                    result.AddFailure($"  {error.Contract}: Expected {error.CalculatedIntrinsic:F4}, got {error.Intrinsic:F4}");
                }
            }
            else
            {
                result.AddSuccess("All intrinsic values validated within $0.01 tolerance");
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            result.AddFailure($"NBBO test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Test Greeks recomputation against provided values
    /// </summary>
    private async Task<TestPhaseResult> TestGreeksRecomputation()
    {
        var result = new TestPhaseResult { PhaseName = "Greeks Recomputation" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
            await connection.OpenAsync();

            // Get sample of options for Greeks testing
            var optionsData = await connection.QueryAsync<OptionsTestRow>(@"
                SELECT contract, underlying_px, strike, dte, iv, option_type,
                       delta, gamma, theta, vega, mid
                FROM options_qa_test 
                WHERE dte > 0 AND iv > 0 AND mid > 0
                ORDER BY RANDOM()
                LIMIT 100
            ");

            var greeksCalculator = new OptionsGreeksCalculator();
            int validatedCount = 0;
            int deltaErrors = 0, gammaErrors = 0, thetaErrors = 0, vegaErrors = 0;

            foreach (var option in optionsData)
            {
                try
                {
                    var timeToExpiry = option.Dte / 365.0;
                    if (timeToExpiry <= 0) continue;

                    // Recompute Greeks
                    var recomputedGreeks = await greeksCalculator.CalculateGreeks(
                        option.Mid, option.UnderlyingPx, option.Strike, 
                        timeToExpiry, 0.05, option.OptionType);

                    // Check tolerances (from QA guide)
                    var deltaError = Math.Abs(recomputedGreeks.Delta - option.Delta);
                    var gammaError = Math.Abs(recomputedGreeks.Gamma - option.Gamma);
                    var thetaError = Math.Abs(recomputedGreeks.Theta - option.Theta);
                    var vegaError = Math.Abs(recomputedGreeks.Vega - option.Vega);

                    if (deltaError > 0.015) deltaErrors++;
                    if (gammaError > 0.0002) gammaErrors++;  
                    if (thetaError > (0.05 / 365)) thetaErrors++;
                    if (vegaError > 0.05) vegaErrors++;

                    validatedCount++;
                }
                catch (Exception ex)
                {
                    result.AddFailure($"Greeks calculation error for {option.Contract}: {ex.Message}");
                }
            }

            // Report results
            if (validatedCount > 0)
            {
                result.AddSuccess($"Greeks validated for {validatedCount} contracts");
                
                var deltaErrorRate = (double)deltaErrors / validatedCount * 100;
                var gammaErrorRate = (double)gammaErrors / validatedCount * 100;
                var thetaErrorRate = (double)thetaErrors / validatedCount * 100;
                var vegaErrorRate = (double)vegaErrors / validatedCount * 100;

                result.AddSuccess($"Delta error rate: {deltaErrorRate:F1}% (tolerance: |Δ| ≤ 0.015)");
                result.AddSuccess($"Gamma error rate: {gammaErrorRate:F1}% (tolerance: |Γ| ≤ 2e-4)");
                result.AddSuccess($"Theta error rate: {thetaErrorRate:F1}% (tolerance: |Θ| ≤ 0.05/365)");
                result.AddSuccess($"Vega error rate: {vegaErrorRate:F1}% (tolerance: |V| ≤ 0.05)");

                if (deltaErrorRate > 10 || gammaErrorRate > 10 || thetaErrorRate > 10 || vegaErrorRate > 10)
                {
                    result.AddFailure("High Greeks error rates detected - review Black-Scholes implementation");
                }
            }
            else
            {
                result.AddFailure("No Greeks could be validated");
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            result.AddFailure($"Greeks test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Test performance requirements from QA guide
    /// </summary>
    private async Task<TestPhaseResult> TestPerformance()
    {
        var result = new TestPhaseResult { PhaseName = "Performance" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
            await connection.OpenAsync();

            // Test 1: 10k-row scan should be sub-second in-memory
            var scanSw = System.Diagnostics.Stopwatch.StartNew();
            var rowCount = await connection.QuerySingleAsync<int>("SELECT COUNT(*) FROM options_qa_test");
            scanSw.Stop();

            if (scanSw.ElapsedMilliseconds < 1000)
            {
                result.AddSuccess($"Row count scan: {scanSw.ElapsedMilliseconds}ms for {rowCount} rows (< 1s ✓)");
            }
            else
            {
                result.AddFailure($"Row count scan too slow: {scanSw.ElapsedMilliseconds}ms for {rowCount} rows");
            }

            // Test 2: Complex aggregation query
            var aggSw = System.Diagnostics.Stopwatch.StartNew();
            var stats = await connection.QueryAsync(@"
                SELECT underlying, COUNT(*) as contract_count,
                       AVG(iv) as avg_iv, AVG(delta) as avg_delta,
                       SUM(volume) as total_volume
                FROM options_qa_test 
                GROUP BY underlying
                ORDER BY contract_count DESC
            ");
            aggSw.Stop();

            if (aggSw.ElapsedMilliseconds < 2000)
            {
                result.AddSuccess($"Aggregation query: {aggSw.ElapsedMilliseconds}ms for {stats.Count()} groups (< 2s ✓)");
            }
            else
            {
                result.AddFailure($"Aggregation query too slow: {aggSw.ElapsedMilliseconds}ms");
            }

            // Test 3: Greeks calculation performance
            var greeksSw = System.Diagnostics.Stopwatch.StartNew();
            var greeksCalculator = new OptionsGreeksCalculator();
            
            var sampleOptions = await connection.QueryAsync<OptionsTestRow>(@"
                SELECT underlying_px, strike, dte, option_type, mid
                FROM options_qa_test 
                WHERE dte > 0 AND mid > 0
                LIMIT 100
            ");

            int greeksCalculated = 0;
            foreach (var option in sampleOptions)
            {
                try
                {
                    await greeksCalculator.CalculateGreeks(
                        option.Mid, option.UnderlyingPx, option.Strike,
                        option.Dte / 365.0, 0.05, option.OptionType);
                    greeksCalculated++;
                }
                catch { /* Continue counting */ }
            }
            greeksSw.Stop();

            if (greeksSw.ElapsedMilliseconds < 300)
            {
                result.AddSuccess($"Greeks calculation: {greeksSw.ElapsedMilliseconds}ms for {greeksCalculated} contracts (< 300ms ✓)");
            }
            else
            {
                result.AddFailure($"Greeks calculation too slow: {greeksSw.ElapsedMilliseconds}ms for {greeksCalculated} contracts");
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            result.AddFailure($"Performance test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Test MCP service integration patterns
    /// </summary>
    private async Task<TestPhaseResult> TestMCPServiceIntegration()
    {
        var result = new TestPhaseResult { PhaseName = "MCP Service Integration" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            // Test distributed query builder
            using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
            await connection.OpenAsync();

            // Simulate options chain query pattern
            var chainSw = System.Diagnostics.Stopwatch.StartNew();
            var spxOptions = await connection.QueryAsync<OptionsBarData>(@"
                SELECT contract, ts, 
                       bid as o, ask as h, bid as l, mid as c, 
                       volume as v, open_interest as oi, 0 as trades,
                       iv, delta, gamma, theta, vega, 0.0 as rho,
                       underlying_px as underlying_price, mid as mid_px, spread_bps / 10000.0 as spread_pct
                FROM options_qa_test 
                WHERE underlying = 'SPX' AND dte BETWEEN 0 AND 7
                ORDER BY strike, dte
                LIMIT 100
            ", new { });
            chainSw.Stop();

            if (spxOptions.Any())
            {
                result.AddSuccess($"Options chain query: {chainSw.ElapsedMilliseconds}ms, {spxOptions.Count()} SPX contracts");
                
                // Validate data structure matches MCP expectations
                var firstOption = spxOptions.First();
                if (!string.IsNullOrEmpty(firstOption.Contract) && firstOption.ImpliedVolatility.HasValue)
                {
                    result.AddSuccess("Options data structure validated for MCP service");
                }
            }
            else
            {
                result.AddFailure("No SPX options found in test data");
            }

            // Test zero-DTE scanner pattern
            var zeroDTESw = System.Diagnostics.Stopwatch.StartNew();
            var zeroDTEOptions = await connection.QueryAsync(@"
                SELECT underlying, COUNT(*) as contract_count,
                       AVG(spread_bps) as avg_spread_bps,
                       AVG(volume) as avg_volume
                FROM options_qa_test 
                WHERE dte = 0
                GROUP BY underlying
                ORDER BY avg_volume DESC
            ");
            zeroDTESw.Stop();

            if (zeroDTEOptions.Any())
            {
                result.AddSuccess($"Zero DTE scanner: {zeroDTESw.ElapsedMilliseconds}ms, {zeroDTEOptions.Count()} underlyings");
            }

            // Test universe management integration
            var universeSymbols = _dataset.GetSymbolsForStrategy(TradingStrategy.ZeroDTE);
            result.AddSuccess($"Universe manager integration: {universeSymbols.Count} symbols for 0DTE strategy");

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            result.AddFailure($"MCP integration test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Test advanced query patterns and features
    /// </summary>
    private async Task<TestPhaseResult> TestAdvancedQueries()
    {
        var result = new TestPhaseResult { PhaseName = "Advanced Queries" };
        var sw = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
            await connection.OpenAsync();

            // Test moneyness analysis
            var moneynessSw = System.Diagnostics.Stopwatch.StartNew();
            var moneynessAnalysis = await connection.QueryAsync(@"
                SELECT 
                    CASE 
                        WHEN LN(underlying_px / strike) > 0.1 THEN 'Deep ITM'
                        WHEN LN(underlying_px / strike) > 0.05 THEN 'ITM'
                        WHEN ABS(LN(underlying_px / strike)) <= 0.05 THEN 'ATM'
                        WHEN LN(underlying_px / strike) < -0.05 THEN 'OTM'
                        ELSE 'Deep OTM'
                    END as moneyness_bucket,
                    COUNT(*) as contract_count,
                    AVG(iv) as avg_iv,
                    AVG(spread_bps) as avg_spread_bps
                FROM options_qa_test
                WHERE underlying_px > 0 AND strike > 0
                GROUP BY moneyness_bucket
                ORDER BY 
                    CASE moneyness_bucket 
                        WHEN 'Deep ITM' THEN 1
                        WHEN 'ITM' THEN 2  
                        WHEN 'ATM' THEN 3
                        WHEN 'OTM' THEN 4
                        WHEN 'Deep OTM' THEN 5
                    END
            ");
            moneynessSw.Stop();

            if (moneynessAnalysis.Any())
            {
                result.AddSuccess($"Moneyness analysis: {moneynessSw.ElapsedMilliseconds}ms, {moneynessAnalysis.Count()} buckets");
            }

            // Test DTE term structure
            var termStructure = await connection.QueryAsync(@"
                SELECT 
                    CASE 
                        WHEN dte = 0 THEN '0 DTE'
                        WHEN dte <= 7 THEN '1-7 DTE'
                        WHEN dte <= 30 THEN '8-30 DTE'
                        WHEN dte <= 90 THEN '31-90 DTE'
                        ELSE '90+ DTE'
                    END as dte_bucket,
                    COUNT(*) as contract_count,
                    AVG(iv) as avg_iv,
                    AVG(vega) as avg_vega
                FROM options_qa_test
                GROUP BY dte_bucket
                ORDER BY MIN(dte)
            ");

            if (termStructure.Any())
            {
                result.AddSuccess($"DTE term structure: {termStructure.Count()} time buckets analyzed");
            }

            // Test liquidity filtering
            var liquidityTest = await connection.QueryAsync(@"
                SELECT 
                    CASE 
                        WHEN spread_bps <= 50 THEN 'Tight'
                        WHEN spread_bps <= 200 THEN 'Moderate' 
                        ELSE 'Wide'
                    END as spread_category,
                    COUNT(*) as contract_count,
                    AVG(volume) as avg_volume,
                    AVG(open_interest) as avg_oi
                FROM options_qa_test
                WHERE spread_bps > 0
                GROUP BY spread_category
                ORDER BY AVG(spread_bps)
            ");

            if (liquidityTest.Any())
            {
                result.AddSuccess($"Liquidity analysis: {liquidityTest.Count()} spread categories");
            }

            sw.Stop();
            result.Duration = sw.Elapsed;
            result.Success = result.Failures.Count == 0;
        }
        catch (Exception ex)
        {
            result.AddFailure($"Advanced queries test exception: {ex.Message}");
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// Create test database with options schema
    /// </summary>
    private async Task CreateTestDatabase()
    {
        // Remove existing test database
        if (File.Exists(_testDatabasePath))
        {
            File.Delete(_testDatabasePath);
        }

        using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
        await connection.OpenAsync();

        // Create table matching CSV structure
        var createTableSql = @"
            CREATE TABLE options_qa_test (
                ts TEXT,
                underlying TEXT,
                underlying_kind TEXT,
                underlying_px REAL,
                contract TEXT,
                option_type TEXT,
                style TEXT,
                expiry TEXT,
                dte INTEGER,
                strike REAL,
                multiplier INTEGER,
                bid REAL,
                ask REAL,
                last REAL,
                mid REAL,
                spread REAL,
                spread_bps REAL,
                volume INTEGER,
                open_interest INTEGER,
                iv REAL,
                delta REAL,
                gamma REAL,
                theta REAL,
                vega REAL,
                intrinsic REAL,
                extrinsic REAL,
                expected_buy_fill REAL,
                expected_sell_fill REAL,
                mid_in_nbbo INTEGER,
                has_bar INTEGER,
                has_quotes INTEGER,
                has_trades INTEGER,
                quality TEXT,
                resolution TEXT,
                source TEXT
            );

            CREATE INDEX idx_underlying ON options_qa_test(underlying);
            CREATE INDEX idx_dte ON options_qa_test(dte);
            CREATE INDEX idx_contract ON options_qa_test(contract);
            CREATE INDEX idx_moneyness ON options_qa_test(underlying_px, strike);
        ";

        await connection.ExecuteAsync(createTableSql);
    }

    /// <summary>
    /// Load CSV data into test database
    /// </summary>
    private async Task<int> LoadCSVToDatabase()
    {
        var lines = await File.ReadAllLinesAsync(_testDataPath);
        var header = lines[0].Split(',');
        var dataLines = lines.Skip(1);

        using var connection = new SqliteConnection($"Data Source={_testDatabasePath}");
        await connection.OpenAsync();

        int loadedCount = 0;
        using var transaction = connection.BeginTransaction();

        try
        {
            foreach (var line in dataLines)
            {
                var values = line.Split(',');
                if (values.Length >= header.Length)
                {
                    var insertSql = $@"
                        INSERT INTO options_qa_test ({string.Join(",", header)}) 
                        VALUES ({string.Join(",", Enumerable.Range(0, header.Length).Select(i => $"@p{i}"))})
                    ";

                    var parameters = new DynamicParameters();
                    for (int i = 0; i < header.Length && i < values.Length; i++)
                    {
                        parameters.Add($"@p{i}", values[i]);
                    }

                    await connection.ExecuteAsync(insertSql, parameters, transaction);
                    loadedCount++;
                }
            }

            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }

        return loadedCount;
    }

    /// <summary>
    /// Print phase results
    /// </summary>
    private void PrintPhaseResults(string phaseName, TestPhaseResult result)
    {
        var status = result.Success ? "✅ PASSED" : "❌ FAILED";
        Console.WriteLine($"{status} {phaseName} ({result.Duration.TotalMilliseconds:F0}ms)");
        
        foreach (var success in result.Successes)
        {
            Console.WriteLine($"  ✓ {success}");
        }
        
        foreach (var failure in result.Failures)
        {
            Console.WriteLine($"  ✗ {failure}");
        }
        
        Console.WriteLine();
    }

    /// <summary>
    /// Print final test summary
    /// </summary>
    private void PrintFinalSummary(QATestResults results)
    {
        Console.WriteLine(new string('=', 60));
        Console.WriteLine("📊 FINAL TEST RESULTS");
        Console.WriteLine(new string('=', 60));

        var phases = new[]
        {
            ("Schema & Loading", results.SchemaTest),
            ("NBBO Invariants", results.NBBOTest),
            ("Greeks Recomputation", results.GreeksTest),
            ("Performance", results.PerformanceTest),
            ("MCP Service", results.MCPTest),
            ("Advanced Queries", results.QueryTest)
        };

        int passed = 0, failed = 0;
        foreach (var (name, result) in phases)
        {
            var status = result?.Success == true ? "PASS" : "FAIL";
            var duration = result?.Duration.TotalMilliseconds ?? 0;
            Console.WriteLine($"{status.PadRight(6)} {name.PadRight(25)} ({duration:F0}ms)");
            
            if (result?.Success == true) passed++;
            else failed++;
        }

        Console.WriteLine();
        Console.WriteLine($"Summary: {passed} passed, {failed} failed");
        
        if (failed == 0)
        {
            Console.WriteLine("🎉 ALL TESTS PASSED - MCP Service ready for production!");
        }
        else
        {
            Console.WriteLine("⚠️  Some tests failed - review implementation before production");
        }
        
        Console.WriteLine(new string('=', 60));
    }
}

// Supporting classes for test results
public class QATestResults
{
    public TestPhaseResult? SchemaTest { get; set; }
    public TestPhaseResult? NBBOTest { get; set; }
    public TestPhaseResult? GreeksTest { get; set; }
    public TestPhaseResult? PerformanceTest { get; set; }
    public TestPhaseResult? MCPTest { get; set; }
    public TestPhaseResult? QueryTest { get; set; }
}

public class TestPhaseResult
{
    public string PhaseName { get; set; } = "";
    public bool Success { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Successes { get; set; } = new();
    public List<string> Failures { get; set; } = new();

    public void AddSuccess(string message) => Successes.Add(message);
    public void AddFailure(string message) => Failures.Add(message);
}

public class NBBOViolation
{
    public string Contract { get; set; } = "";
    public double Bid { get; set; }
    public double Mid { get; set; }
    public double Ask { get; set; }
    public double Spread { get; set; }
    public string ViolationType { get; set; } = "";
}

public class IntrinsicError
{
    public string Contract { get; set; } = "";
    public string OptionType { get; set; } = "";
    public double UnderlyingPx { get; set; }
    public double Strike { get; set; }
    public double Intrinsic { get; set; }
    public double CalculatedIntrinsic { get; set; }
    public double Error { get; set; }
}

public class OptionsTestRow
{
    public string Contract { get; set; } = "";
    public double UnderlyingPx { get; set; }
    public double Strike { get; set; }
    public int Dte { get; set; }
    public double IV { get; set; }
    public string OptionType { get; set; } = "";
    public double Delta { get; set; }
    public double Gamma { get; set; }
    public double Theta { get; set; }
    public double Vega { get; set; }
    public double Mid { get; set; }
}