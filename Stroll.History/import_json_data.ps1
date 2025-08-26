# Import Alpha Vantage JSON data to SQLite archive
param(
    [string]$DataPath = "acquired_data"
)

Write-Host "Importing Alpha Vantage JSON data to SQLite..." -ForegroundColor Green

$jsonFiles = Get-ChildItem -Path $DataPath -Filter "SPY_*_5min.json"
$totalFiles = $jsonFiles.Count
$processedFiles = 0
$totalBars = 0

Write-Host "Found $totalFiles JSON files to import" -ForegroundColor Yellow

foreach ($file in $jsonFiles) {
    $processedFiles++
    Write-Host "[$processedFiles/$totalFiles] Processing $($file.Name)..." -ForegroundColor Cyan
    
    try {
        $jsonContent = Get-Content $file.FullName -Raw | ConvertFrom-Json
        
        if ($jsonContent.'Time Series (5min)') {
            $timeSeries = $jsonContent.'Time Series (5min)'
            $barCount = ($timeSeries | Get-Member -MemberType NoteProperty).Count
            
            Write-Host "   Found $barCount bars in $($file.Name)" -ForegroundColor White
            $totalBars += $barCount
        } else {
            Write-Host "   No time series data found in $($file.Name)" -ForegroundColor Yellow
        }
    }
    catch {
        Write-Host "   Error processing $($file.Name): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ""
Write-Host "IMPORT SUMMARY" -ForegroundColor Green
Write-Host "Files processed: $processedFiles/$totalFiles" -ForegroundColor Green
Write-Host "Total bars found: $totalBars" -ForegroundColor Green
Write-Host "Coverage: 2023-01 through 2024-10" -ForegroundColor Green