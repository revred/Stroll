# Smart Data Acquisition - Only Missing Months
# Checks what we already have to avoid duplicate API calls

$API_KEY = "OI8FMGPE6K95H697"
$SYMBOL = "SPY"
$BASE_URL = "https://www.alphavantage.co/query"
$DATA_DIR = "acquired_data"

Write-Host "🔍 SMART DATA ACQUISITION" -ForegroundColor Green
Write-Host "=========================" -ForegroundColor Green

# Check what we already have
$existingFiles = Get-ChildItem -Path $DATA_DIR -Filter "SPY_*.json" -ErrorAction SilentlyContinue
$existingMonths = @()

foreach ($file in $existingFiles) {
    if ($file.Name -match "SPY_(\d{4})_(\d{2})_5min\.json") {
        $existingMonths += "$($matches[1])-$($matches[2])"
    }
}

Write-Host "📋 Already acquired months:" -ForegroundColor Yellow
$existingMonths | Sort-Object | ForEach-Object { Write-Host "   ✅ $_" -ForegroundColor Green }

# Define what we need (based on DATA_ACQUISITION_STATUS.md)
$targetMonths = @("2024-11", "2024-12")

# Filter out what we already have
$missingMonths = $targetMonths | Where-Object { $_ -notin $existingMonths }

if ($missingMonths.Count -eq 0) {
    Write-Host "🎉 All target months already acquired! No API calls needed." -ForegroundColor Green
    exit 0
}

Write-Host ""
Write-Host "📥 Missing months to acquire:" -ForegroundColor Cyan
$missingMonths | ForEach-Object { Write-Host "   ❌ $_" -ForegroundColor Red }

Write-Host ""
Write-Host "📞 Will use $($missingMonths.Count) API calls out of 25 daily quota" -ForegroundColor Yellow

# Confirm before proceeding
Write-Host "Continue with acquisition? (Y/N): " -NoNewline -ForegroundColor White
$response = Read-Host
if ($response -ne "Y" -and $response -ne "y") {
    Write-Host "❌ Acquisition cancelled by user" -ForegroundColor Red
    exit 1
}

Write-Host ""
$callCount = 0

foreach ($month in $missingMonths) {
    Write-Host "📥 Acquiring $month..." -ForegroundColor Blue
    
    try {
        $url = "$BASE_URL" + "?function=TIME_SERIES_INTRADAY" + "&symbol=$SYMBOL" + "&interval=5min" + "&apikey=$API_KEY" + "&outputsize=full" + "&month=$month"
        
        $year = $month.Split('-')[0]
        $monthNum = $month.Split('-')[1]
        $filename = "$DATA_DIR\SPY_$($year)_$($monthNum)_5min.json"
        
        $response = Invoke-RestMethod -Uri $url -Method Get
        $callCount++
        
        if ($response.'Time Series (5min)') {
            $barCount = $response.'Time Series (5min)'.PSObject.Properties.Count
            Write-Host "✅ $month`: $barCount bars acquired" -ForegroundColor Green
            
            # Save file
            $response | ConvertTo-Json -Depth 10 | Out-File -FilePath $filename -Encoding UTF8
            Write-Host "   💾 Saved: $filename" -ForegroundColor Gray
        }
        else {
            Write-Host "❌ $month`: No data - $($response.Information)" -ForegroundColor Red
            
            # Don't save empty/error responses
            if (Test-Path $filename) {
                Remove-Item $filename
            }
        }
        
        # Rate limiting
        if ($missingMonths.IndexOf($month) -lt ($missingMonths.Count - 1)) {
            Write-Host "   ⏳ Rate limiting - waiting 15 seconds..." -ForegroundColor Gray
            Start-Sleep -Seconds 15
        }
    }
    catch {
        Write-Host "❌ $month`: Error - $($_.Exception.Message)" -ForegroundColor Red
        $callCount++
    }
}

Write-Host ""
Write-Host "📊 ACQUISITION COMPLETE" -ForegroundColor Green
Write-Host "========================" -ForegroundColor Green
Write-Host "📞 API Calls Used: $callCount" -ForegroundColor Yellow
Write-Host "💾 Check acquired_data/ folder for new files" -ForegroundColor Cyan
Write-Host ""
Write-Host "📝 Next: Update DATA_ACQUISITION_STATUS.md with results" -ForegroundColor White