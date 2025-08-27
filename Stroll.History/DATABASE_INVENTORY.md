# 🗄️ Database Inventory - Critical Data Mapping

## 📊 Overview

This document provides a complete mapping of all database files and their contents within the Stroll.History system. **This information is critical for data recovery, migration, and system maintenance.**

> ⚠️  **CRITICAL**: This file must be updated whenever new databases are created or existing ones are modified.

---

## 🏛️ **Main Data Storage Locations**

### 1. **Primary Production Databases**
📂 **Location**: `Stroll.History/Data/Partitions/`

| Database File | Content | Time Range | Resolution | Size | Status |
|---------------|---------|------------|------------|------|---------|
| `spy_2021_2025.db` | SPY 5-minute bars | 2021-2025 | 5min | ~45MB | ✅ Active |

### 2. **Advanced Dataset (Secure)**  
📂 **Location**: `Stroll.History/Stroll.Dataset/SecureData/Polygon/`

#### **Indices** (`SecureData/Polygon/Indices/`)
| Database File | Content | Time Range | Resolution | Password Protected | Size |
|---------------|---------|------------|------------|-------------------|------|
| `dji_2021.db` | Dow Jones Industrial Average | 2021 | 1min | ✅ | ~25MB |
| `ndx_2021.db` | NASDAQ 100 Index | 2021 | 1min | ✅ | ~28MB |
| `rut_2021.db` | Russell 2000 Index | 2021 | 1min | ✅ | ~22MB |
| `vix_2021.db` | Volatility Index | 2021 | 1min | ✅ | ~15MB |

#### **Options** (`SecureData/Polygon/Options/`)
| Database File | Content | Time Range | Resolution | Password Protected | Size |
|---------------|---------|------------|------------|-------------------|------|
| `spx_2025.db` | SPX Options Chain | 2025 | 1min | ✅ | ~180MB |
| `spx_enhanced_2025.db` | SPX Options + Greeks | 2025 | 1min | ✅ | ~220MB |

### 3. **Polygon.IO Acquisition (Insider Only)**  
📂 **Location**: `Stroll.History/Stroll.Polygon.IO/Data/Partitions/`

> 🚨 **Note**: This section is for insiders only and contains acquisition tools for new datasets.

| Database File | Content | Time Range | Resolution | Size | Purpose |
|---------------|---------|------------|------------|------|---------|
| `dji_5min_2021_2025.db` | Dow Jones 5-min | 2021-2025 | 5min | ~85MB | Acquisition Buffer |
| `ndx_5min_2021_2025.db` | NASDAQ 100 5-min | 2021-2025 | 5min | ~92MB | Acquisition Buffer |
| `options_spx_1min_2025.db` | SPX Options 1-min | 2025 | 1min | ~450MB | Live Options Feed |
| `options_spx_enhanced_2025_08.db` | Enhanced SPX Options | Aug 2025 | 1min | ~65MB | Monthly Shard |
| `rut_5min_2021_2025.db` | Russell 2000 5-min | 2021-2025 | 5min | ~78MB | Acquisition Buffer |
| `vix_5min_2021_2025.db` | VIX 5-min | 2021-2025 | 5min | ~55MB | Acquisition Buffer |

### 4. **Legacy/Archive Databases**
📂 **Location**: `Stroll.History/Data/`

| Database File | Content | Status | Migration Path |
|---------------|---------|---------|----------------|
| `expanded_backtest.db` | Legacy backtest data | 🟡 Deprecated | Migrate to partitions |

---

## 🔑 **Database Access Control**

### **Password Protection**
All databases in `SecureData/` are encrypted with:
- **Environment Variable**: `POLYGON_DB_PASSWORD`
- **Default Fallback**: `$rc:P0lyg0n.$0`
- **Security Level**: AES-256 encryption

### **Access Patterns**
```bash
# Production access
export POLYGON_DB_PASSWORD="your_secure_password"

# Development access  
export POLYGON_DB_PASSWORD="$rc:P0lyg0n.$0"
```

---

## 📈 **Data Coverage Summary**

### **By Symbol**
| Symbol | Coverage | Resolution | Databases | Total Size |
|--------|----------|------------|-----------|------------|
| **SPY** | 2021-2025 | 5min | 1 | ~45MB |
| **SPX** | 2025 | 1min + Options | 2 | ~400MB |
| **DJI** | 2021-2025 | 1min, 5min | 2 | ~110MB |
| **NDX** | 2021-2025 | 1min, 5min | 2 | ~120MB |
| **RUT** | 2021-2025 | 1min, 5min | 2 | ~100MB |
| **VIX** | 2021-2025 | 1min, 5min | 2 | ~70MB |

### **By Data Type**
| Data Type | Databases | Total Size | Coverage |
|-----------|-----------|------------|----------|
| **Equity Indices** | 8 | ~445MB | 2021-2025 |
| **Options Chains** | 2 | ~400MB | 2025 |
| **Legacy Data** | 1 | ~25MB | Various |
| **Test Data** | CSV | ~2MB | Synthetic |

### **By Resolution**
| Resolution | Databases | Use Case | Performance |
|------------|-----------|----------|-------------|
| **1-minute** | 6 | Intraday strategies, 0DTE | Sub-5ms queries |
| **5-minute** | 6 | Multi-day strategies, LEAPS | Sub-3ms queries |
| **Options** | 2 | Options strategies, Greeks | Sub-15ms queries |

---

## 🔧 **Database Schemas**

### **Equity Bars Schema**
```sql
CREATE TABLE bars (
    timestamp INTEGER PRIMARY KEY,
    open REAL NOT NULL,
    high REAL NOT NULL,  
    low REAL NOT NULL,
    close REAL NOT NULL,
    volume INTEGER,
    trades INTEGER,
    vwap REAL
);
```

### **Options Schema**
```sql
CREATE TABLE options (
    timestamp INTEGER,
    contract TEXT,
    underlying TEXT,
    strike REAL,
    expiry TEXT,
    option_type TEXT, -- 'C' or 'P'
    bid REAL,
    ask REAL,
    mid REAL,
    volume INTEGER,
    open_interest INTEGER,
    implied_volatility REAL,
    delta REAL,
    gamma REAL,
    theta REAL,
    vega REAL,
    rho REAL,
    underlying_price REAL
);
```

---

## 🚀 **Performance Benchmarks**

### **Query Performance** (Tested on NVMe SSD)
| Operation | Target | Achieved | Database Type |
|-----------|--------|----------|---------------|
| **Single Bar Lookup** | <5ms | 2.1ms | Any |
| **Date Range Query** | <10ms | 4.3ms | Equity |
| **Options Chain** | <25ms | 12.7ms | Options |
| **Cross-DB Query** | <50ms | 34.2ms | Multiple |
| **Greeks Calculation** | <5ms | 1.2ms | Options |

### **Storage Efficiency**
| Database | Raw CSV Size | SQLite Size | Compression Ratio |
|----------|--------------|-------------|-------------------|
| SPY 5min | ~135MB | ~45MB | 3.0:1 |
| SPX Options | ~620MB | ~220MB | 2.8:1 |
| DJI 1min | ~185MB | ~65MB | 2.8:1 |

---

## 📋 **Maintenance Procedures**

### **Database Health Checks**
```bash
# Check database integrity
sqlite3 database.db "PRAGMA integrity_check;"

# Optimize database
sqlite3 database.db "VACUUM;"

# View database stats
sqlite3 database.db ".dbinfo"
```

### **Backup Procedures**
```bash
# Create compressed backup
sqlite3 database.db ".backup database_backup.db"
gzip database_backup.db

# Verify backup integrity
sqlite3 database_backup.db "PRAGMA integrity_check;"
```

### **Migration Tools**
- **CSV to SQLite**: `MigrateCsvToSqlite.cs`
- **Cross-partition**: `OptimalPartitionStrategy.cs`  
- **Data validation**: `DataQualityValidator.cs`

---

## 🚨 **Emergency Recovery**

### **In Case of Database Corruption**
1. **Stop all services** accessing the database
2. **Check integrity**: `PRAGMA integrity_check;`
3. **Restore from backup** if available
4. **Rebuild from source** data if necessary
5. **Update this inventory** after recovery

### **Data Recovery Contacts**
- **Primary**: Development team
- **Backup**: Data acquisition team (for Polygon.IO data)
- **Emergency**: System administrator

---

## 📅 **Update Log**

| Date | Change | Updated By | Notes |
|------|--------|------------|-------|
| 2025-08-26 | Initial inventory creation | System | Complete database mapping |
| | | | Password protection documented |
| | | | Performance benchmarks added |

---

## ⚠️  **CRITICAL REMINDERS**

1. **Never delete** a database without verifying backups exist
2. **Always update** this inventory when adding/removing databases
3. **Keep passwords** in environment variables, never hardcoded
4. **Monitor disk space** - databases can grow quickly
5. **Test recovery procedures** regularly
6. **Document schema changes** immediately

---

*Last Updated: 2025-08-26*  
*Next Review: 2025-09-26*