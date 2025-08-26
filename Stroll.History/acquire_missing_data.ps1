# Enhanced Alpha Vantage Data Acquisition Script
# Supports multiple API keys and targeted date ranges

param(
    [string]$ApiKey = "",
    [string]$StartMonth = "2024-11",
    [string]$EndMonth = "2025-07"
)

Write-Host "ENHANCED ALPHA VANTAGE DATA ACQUISITION" -ForegroundColor Green
Write-Host "=============================================" -ForegroundColor Green
Write-Host ""

# Validate API key
if ([string]::IsNullOrWhiteSpace($ApiKey)) {
    Write-Host "ERROR: API key is required" -ForegroundColor Red
    Write-Host "Usage: .\acquire_missing_data.ps1 -ApiKey 'YOUR_API_KEY' [-StartMonth '2024-11'] [-EndMonth '2025-07']" -ForegroundColor Yellow
    exit 1
}

Write-Host "Using API Key: $($ApiKey.Substring(0,8))..." -ForegroundColor Cyan
Write-Host "Target Range: $StartMonth to $EndMonth" -ForegroundColor Cyan
Write-Host ""

# Create output directory
$DataDir = "acquired_data"
if (!(Test-Path $DataDir)) {
    New-Item -ItemType Directory -Path $DataDir | Out-Null
    Write-Host "Created directory: $DataDir" -ForegroundColor Green
}

# Generate month list
function Get-MonthRange($start, $end) {
    $startDate = [DateTime]::ParseExact($start, "yyyy-MM", $null)
    $endDate = [DateTime]::ParseExact($end, "yyyy-MM", $null)
    $months = @()
    
    $current = $startDate
    while ($current -le $endDate) {
        $months += $current.ToString("yyyy-MM")
        $current = $current.AddMonths(1)
    }
    
    return $months
}

try {
    $targetMonths = Get-MonthRange $StartMonth $EndMonth
    Write-Host "Target Months: $($targetMonths -join ', ')" -ForegroundColor Yellow
    Write-Host ""
    
    $callCount = 0
    $maxCalls = 25
    $successCount = 0
    $failureCount = 0
    
    foreach ($month in $targetMonths) {
        if ($callCount -ge $maxCalls) {
            Write-Host "WARNING: Reached daily API call limit ($maxCalls)" -ForegroundColor Yellow
            break
        }
        
        $callCount++
        $fileName = "SPY_$($month.Replace('-', '_'))_5min.json"
        $filePath = Join-Path $DataDir $fileName
        
        # Skip if file already exists and has content
        if (Test-Path $filePath) {
            $fileInfo = Get-Item $filePath
            if ($fileInfo.Length -gt 1000) {
                Write-Host "Skipping $month (already exists, $([math]::Round($fileInfo.Length/1KB, 1)) KB)" -ForegroundColor Gray
                continue
            }
        }
        
        Write-Host "Fetching $month (Call $callCount/$maxCalls)..." -ForegroundColor Cyan
        
        # Construct API URL
        $url = "https://www.alphavantage.co/query?function=TIME_SERIES_INTRADAY&symbol=SPY&interval=5min&outputsize=full&month=$month&apikey=$ApiKey"
        
        try {
            # Download data
            Invoke-WebRequest -Uri $url -OutFile $filePath -TimeoutSec 30
            
            # Verify download
            if (Test-Path $filePath) {
                $fileInfo = Get-Item $filePath
                $content = Get-Content $filePath -Raw | ConvertFrom-Json -ErrorAction SilentlyContinue
                
                if ($content -and $content.'Time Series (5min)') {
                    $barCount = ($content.'Time Series (5min)' | Get-Member -MemberType NoteProperty).Count
                    Write-Host "   SUCCESS: $barCount bars, $([math]::Round($fileInfo.Length/1KB, 1)) KB" -ForegroundColor Green
                    $successCount++
                } elseif ($content -and $content.'Error Message') {
                    Write-Host "   API ERROR: $($content.'Error Message')" -ForegroundColor Red
                    Remove-Item $filePath -Force
                    $failureCount++
                } elseif ($content -and $content.'Note') {
                    Write-Host "   API LIMIT: $($content.'Note')" -ForegroundColor Yellow
                    Remove-Item $filePath -Force
                    Write-Host "   Stopping due to API rate limit" -ForegroundColor Yellow
                    break
                } else {
                    Write-Host "   UNKNOWN: Invalid JSON response" -ForegroundColor Yellow
                    $failureCount++
                }
            } else {
                Write-Host "   FAILED: File not created" -ForegroundColor Red
                $failureCount++
            }
        }
        catch {
            Write-Host "   EXCEPTION: $($_.Exception.Message)" -ForegroundColor Red
            $failureCount++
        }
        
        # Rate limiting delay (except for last call)
        if ($callCount -lt $maxCalls -and $callCount -lt $targetMonths.Count) {
            Write-Host "   Waiting 12 seconds..." -ForegroundColor Gray
            Start-Sleep -Seconds 12
        }
    }
    
    Write-Host ""
    Write-Host "ACQUISITION SUMMARY" -ForegroundColor Green
    Write-Host "======================" -ForegroundColor Green
    Write-Host "API Calls Used: $callCount/$maxCalls" -ForegroundColor White
    Write-Host "Successful: $successCount" -ForegroundColor Green
    Write-Host "Failed: $failureCount" -ForegroundColor Red
    Write-Host "Data Location: $(Resolve-Path $DataDir)" -ForegroundColor White
    Write-Host ""
    
    if ($successCount -gt 0) {
        Write-Host "Next Steps:" -ForegroundColor Yellow
        Write-Host "1. Run: .\import_json_data.ps1 to validate acquired data" -ForegroundColor White
        Write-Host "2. Update SQLite database with new data" -ForegroundColor White
        Write-Host "3. Run expanded backtest with increased dataset" -ForegroundColor White
    }
    
    Write-Host ""
    Write-Host "Data acquisition completed!" -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "SCRIPT ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}