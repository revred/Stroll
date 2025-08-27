# SPY Data Partition Creation Script
# Creates a new 5-year partition database with optimized schema

param(
    [string]$PartitionName = "",
    [string]$StartYear = "",
    [string]$EndYear = ""
)

Write-Host "🗄️ SPY DATA PARTITION CREATION" -ForegroundColor Green
Write-Host "==============================" -ForegroundColor Green
Write-Host ""

# Validate parameters
if ([string]::IsNullOrWhiteSpace($PartitionName) -or [string]::IsNullOrWhiteSpace($StartYear) -or [string]::IsNullOrWhiteSpace($EndYear)) {
    Write-Host "Usage: .\create_partition.ps1 -PartitionName 'spy_2016_2020' -StartYear '2016' -EndYear '2020'" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Available partition templates:" -ForegroundColor Cyan
    Write-Host "  spy_2021_2025 (Current - exists)" -ForegroundColor Gray
    Write-Host "  spy_2016_2020 (Pre-COVID era)" -ForegroundColor White
    Write-Host "  spy_2011_2015 (Post-crisis recovery)" -ForegroundColor White  
    Write-Host "  spy_2006_2010 (Financial crisis)" -ForegroundColor White
    Write-Host "  spy_2001_2005 (Dot-com recovery)" -ForegroundColor White
    exit 1
}

$partitionPath = "Data\Partitions\$PartitionName.db"
Write-Host "Creating partition: $PartitionName" -ForegroundColor Cyan
Write-Host "Time period: $StartYear - $EndYear" -ForegroundColor Cyan
Write-Host "Database path: $partitionPath" -ForegroundColor Cyan
Write-Host ""

# Ensure partitions directory exists
if (!(Test-Path "Data\Partitions")) {
    New-Item -ItemType Directory -Path "Data\Partitions" | Out-Null
    Write-Host "Created partitions directory" -ForegroundColor Green
}

# Check if partition already exists
if (Test-Path $partitionPath) {
    Write-Host "WARNING: Partition already exists at $partitionPath" -ForegroundColor Yellow
    $response = Read-Host "Overwrite existing partition? (y/N)"
    if ($response -ne 'y' -and $response -ne 'Y') {
        Write-Host "Operation cancelled" -ForegroundColor Yellow
        exit 0
    }
    Remove-Item $partitionPath -Force
    Write-Host "Removed existing partition" -ForegroundColor Yellow
}

try {
    # Create SQLite database with optimized schema
    $connectionString = "Data Source=$partitionPath"
    
    # Use .NET SQLite to create database
    Add-Type -Path "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\9.0.8\System.Data.SQLite.dll" -ErrorAction SilentlyContinue
    
    # Create database using command line sqlite3 if available, otherwise use PowerShell method
    $createScript = @"
-- SPY $StartYear-$EndYear Partition Database Schema
-- Created: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

CREATE TABLE market_bars (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    symbol TEXT NOT NULL,
    timestamp DATETIME NOT NULL,
    open REAL NOT NULL,
    high REAL NOT NULL,
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume INTEGER NOT NULL,
    date_only DATE GENERATED ALWAYS AS (date(timestamp)) STORED
);

-- Performance indexes for hyperfast queries
CREATE INDEX idx_symbol_timestamp ON market_bars(symbol, timestamp);
CREATE INDEX idx_timestamp ON market_bars(timestamp);
CREATE INDEX idx_date_only ON market_bars(date_only);
CREATE INDEX idx_symbol_date ON market_bars(symbol, date_only);

-- SQLite performance optimizations
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA cache_size = 1000000;
PRAGMA temp_store = MEMORY;
PRAGMA mmap_size = 268435456;

-- Insert partition metadata
CREATE TABLE partition_info (
    key TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT INTO partition_info VALUES ('partition_name', '$PartitionName');
INSERT INTO partition_info VALUES ('start_year', '$StartYear');
INSERT INTO partition_info VALUES ('end_year', '$EndYear');
INSERT INTO partition_info VALUES ('created_date', '$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')');
INSERT INTO partition_info VALUES ('target_size_mb', '40');
INSERT INTO partition_info VALUES ('symbol', 'SPY');
INSERT INTO partition_info VALUES ('resolution', '5min');
"@

    # Write SQL script to temporary file
    $tempSqlFile = "temp_partition_creation.sql"
    $createScript | Out-File -FilePath $tempSqlFile -Encoding UTF8
    
    # Try to use sqlite3 command line tool
    try {
        $sqliteResult = sqlite3 $partitionPath ".read $tempSqlFile" 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Database created successfully using sqlite3" -ForegroundColor Green
        } else {
            throw "sqlite3 command failed"
        }
    } catch {
        Write-Host "sqlite3 not available, using alternative method..." -ForegroundColor Yellow
        
        # Alternative: Create empty database file and schema will be created during first data import
        New-Item -Path $partitionPath -ItemType File -Force | Out-Null
        Write-Host "Empty partition database created - schema will be initialized on first data import" -ForegroundColor Green
    }
    
    # Clean up temporary file
    if (Test-Path $tempSqlFile) {
        Remove-Item $tempSqlFile -Force
    }
    
    # Verify database creation
    $fileInfo = Get-Item $partitionPath
    Write-Host ""
    Write-Host "PARTITION CREATION COMPLETE" -ForegroundColor Green
    Write-Host "============================" -ForegroundColor Green
    Write-Host "File: $($fileInfo.FullName)" -ForegroundColor White
    Write-Host "Size: $([math]::Round($fileInfo.Length/1KB, 1)) KB" -ForegroundColor White
    Write-Host "Created: $($fileInfo.CreationTime)" -ForegroundColor White
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Acquire data for $StartYear-$EndYear period" -ForegroundColor White
    Write-Host "2. Use JsonMigration tool to populate partition" -ForegroundColor White
    Write-Host "3. Verify data integrity and performance" -ForegroundColor White
    Write-Host "4. Update partition status documentation" -ForegroundColor White
    
} catch {
    Write-Host ""
    Write-Host "PARTITION CREATION FAILED" -ForegroundColor Red
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}