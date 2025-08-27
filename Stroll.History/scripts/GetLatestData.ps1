# Strategic Alpha Vantage Data Acquisition
# Maximize 25 API call quota to get latest missing years

$API_KEY = "OI8FMGPE6K95H697"
$MAX_CALLS = 25
$SYMBOL = "SPY"
$BASE_URL = "https://www.alphavantage.co/query"

Write-Host "🎯 STRATEGIC DATA ACQUISITION" -ForegroundColor Green
Write-Host "=============================" -ForegroundColor Green
Write-Host "📊 API Call Quota: $MAX_CALLS" -ForegroundColor Yellow
Write-Host "🎯 Target: Latest missing years (2023-2025)" -ForegroundColor Yellow
Write-Host ""

# Create output directory
$outputDir = "acquired_data"
if (!(Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir
}

# Target recent months first (most valuable data)
$currentDate = Get-Date
$targetMonths = @()

# Get last 25 months (working backwards from current)
for ($i = 0; $i -lt $MAX_CALLS; $i++) {
    $targetDate = $currentDate.AddMonths(-$i)
    $targetMonths += @{
        Year = $targetDate.Year
        Month = $targetDate.Month
        MonthName = $targetDate.ToString("yyyy-MM")
    }
}

Write-Host "🎯 Target months (most recent first):" -ForegroundColor Cyan
$targetMonths | Select-Object -First 10 | ForEach-Object {
    Write-Host "   - $($_.MonthName)" -ForegroundColor White
}
if ($targetMonths.Count -gt 10) {
    Write-Host "   ... and $($targetMonths.Count - 10) more" -ForegroundColor Gray
}
Write-Host ""

$callCount = 0
$successfulMonths = @()
$failedMonths = @()

foreach ($month in $targetMonths) {
    if ($callCount -ge $MAX_CALLS) {
        Write-Host "⚠️  Reached API call limit ($MAX_CALLS)" -ForegroundColor Yellow
        break
    }
    
    $monthStr = $month.MonthName
    Write-Host "📥 Fetching $monthStr (Call $($callCount + 1)/$MAX_CALLS)" -ForegroundColor Blue
    
    try {
        # Alpha Vantage URL with month parameter for targeted data
        $url = "$BASE_URL" + "?function=TIME_SERIES_INTRADAY" + "&symbol=$SYMBOL" + "&interval=5min" + "&apikey=$API_KEY" + "&outputsize=full" + "&month=$monthStr"
        
        $response = Invoke-RestMethod -Uri $url -Method Get
        $callCount++
        
        if ($response.'Time Series (5min)') {
            $barCount = $response.'Time Series (5min)'.PSObject.Properties.Count
            Write-Host "✅ $monthStr`: $barCount bars retrieved" -ForegroundColor Green
            $successfulMonths += $monthStr
            
            # Save to JSON file
            $filename = "$outputDir\SPY_$($month.Year)_$($month.Month.ToString('00'))_5min.json"
            $response | ConvertTo-Json -Depth 10 | Out-File -FilePath $filename -Encoding UTF8
            Write-Host "   💾 Saved: $filename" -ForegroundColor Gray
        }
        else {
            Write-Host "❌ $monthStr`: No data returned" -ForegroundColor Red
            $failedMonths += $monthStr
            
            # Check if we hit rate limit
            if ($response.Note -match "rate limit") {
                Write-Host "   ⚠️  Rate limit hit - waiting 60 seconds..." -ForegroundColor Yellow
                Start-Sleep -Seconds 60
            }
        }
        
        # Rate limiting between calls
        if ($callCount -lt $MAX_CALLS) {
            Write-Host "   ⏳ Waiting 15 seconds..." -ForegroundColor Gray
            Start-Sleep -Seconds 15
        }
    }
    catch {
        Write-Host "❌ $monthStr`: Error - $($_.Exception.Message)" -ForegroundColor Red
        $failedMonths += $monthStr
        $callCount++
    }
}

# Summary Report
Write-Host ""
Write-Host "📊 ACQUISITION SUMMARY" -ForegroundColor Green
Write-Host "======================" -ForegroundColor Green
Write-Host "📞 API Calls Used: $callCount/$MAX_CALLS" -ForegroundColor Yellow
Write-Host "✅ Successful: $($successfulMonths.Count) months" -ForegroundColor Green
Write-Host "❌ Failed: $($failedMonths.Count) months" -ForegroundColor Red

if ($successfulMonths.Count -gt 0) {
    Write-Host ""
    Write-Host "✅ Successfully acquired months:" -ForegroundColor Green
    $successfulMonths | ForEach-Object { Write-Host "   - $_" -ForegroundColor White }
    
    Write-Host ""
    Write-Host "📁 Data saved to: ./$outputDir/" -ForegroundColor Cyan
    Write-Host "📝 Next steps:" -ForegroundColor Cyan
    Write-Host "   1. Review acquired data files" -ForegroundColor White
    Write-Host "   2. Import to SQLite using migration tool" -ForegroundColor White
    Write-Host "   3. Run performance benchmarks on expanded dataset" -ForegroundColor White
}

if ($failedMonths.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ Failed months (may retry later):" -ForegroundColor Red
    $failedMonths | ForEach-Object { Write-Host "   - $_" -ForegroundColor White }
}

Write-Host ""
Write-Host "🎯 Strategic acquisition completed!" -ForegroundColor Green