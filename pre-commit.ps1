# Stroll Project Pre-Commit Verification Script
param(
    [string]$ProjectPath = ".",
    [switch]$SkipPerformance = $false
)

Write-Host "PRE-COMMIT VERIFICATION" -ForegroundColor Green
Write-Host "=======================" -ForegroundColor Green

$ErrorActionPreference = "Continue"
$buildFailed = $false
$testsFailed = $false

try {
    # 1. Clean build
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    dotnet clean $ProjectPath --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Clean failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Solution cleaned" -ForegroundColor Green
    
    # 2. Restore packages
    Write-Host "Restoring packages..." -ForegroundColor Yellow
    dotnet restore $ProjectPath --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Package restore failed" -ForegroundColor Red
        exit 1
    }
    Write-Host "Packages restored" -ForegroundColor Green
    
    # 3. Build with warning checks
    Write-Host "Building solution..." -ForegroundColor Yellow
    $buildOutput = dotnet build $ProjectPath --no-restore --verbosity normal 2>&1
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "BUILD FAILED" -ForegroundColor Red
        Write-Host $buildOutput -ForegroundColor Red
        $buildFailed = $true
    }
    
    # Check for warnings
    $warnings = $buildOutput | Where-Object { $_ -match "warning\s+(CS\d+|CA\d+|IDE\d+)" }
    if ($warnings) {
        Write-Host "BUILD WARNINGS DETECTED:" -ForegroundColor Red
        $warnings | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
        $buildFailed = $true
    }
    
    if (!$buildFailed) {
        Write-Host "Build successful - no warnings or errors" -ForegroundColor Green
    }
    
    # 4. Run tests
    if (!$buildFailed) {
        Write-Host "Running all tests..." -ForegroundColor Yellow
        dotnet test $ProjectPath --no-build --verbosity normal
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "TESTS FAILED" -ForegroundColor Red
            $testsFailed = $true
        } else {
            Write-Host "All tests passed" -ForegroundColor Green
        }
    }
    
} catch {
    Write-Host "PRE-COMMIT CHECK FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Final verdict
Write-Host ""
if ($buildFailed -or $testsFailed) {
    Write-Host "COMMIT REJECTED" -ForegroundColor Red
    Write-Host "Fix all issues before committing" -ForegroundColor Red
    exit 1
} else {
    Write-Host "READY TO COMMIT" -ForegroundColor Green
    Write-Host "All checks passed" -ForegroundColor Green
    exit 0
}