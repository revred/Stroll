# SPY 5-Minute Data Partitioning Strategy

## 🎯 Overview

To maintain manageable file sizes for version control and efficient storage, SPY 5-minute historical data is partitioned into **5-year SQLite databases**. Each partition contains approximately **40MB of optimized data** (~250,000 bars) covering complete market cycles.

## 📊 Partition Structure

### Current Implementation
- **File Size Target**: ~40MB per partition
- **Time Span**: 5 years (60 months) per database
- **Data Density**: ~6,250 bars per year (~250,000 bars total)
- **Storage Format**: Optimized SQLite with indexes and WAL journaling

### Partition Schema

| Database File | Time Period | Status | Est. Size | Bars | Coverage |
|---------------|-------------|---------|-----------|------|----------|
| `spy_2021_2025.db` | Jan 2021 - Dec 2025 | **✅ Active** | 40MB | ~250K | Current + Future |
| `spy_2016_2020.db` | Jan 2016 - Dec 2020 | 🔄 Planned | 40MB | ~250K | Pre-COVID Era |
| `spy_2011_2015.db` | Jan 2011 - Dec 2015 | 🔄 Planned | 40MB | ~250K | Post-Crisis Recovery |
| `spy_2006_2010.db` | Jan 2006 - Dec 2010 | 🔄 Planned | 40MB | ~250K | Financial Crisis |
| `spy_2001_2005.db` | Jan 2001 - Dec 2005 | 🔄 Planned | 40MB | ~250K | Dot-Com Recovery |

## 🗄️ Storage Architecture

### Directory Structure
```
Stroll.History/
├── Data/
│   ├── Partitions/
│   │   ├── spy_2021_2025.db     # Current partition (40MB)
│   │   ├── spy_2016_2020.db     # Previous 5 years
│   │   ├── spy_2011_2015.db     # Financial recovery
│   │   ├── spy_2006_2010.db     # Financial crisis
│   │   └── spy_2001_2005.db     # Early 2000s
│   └── Staging/                 # Temporary migration files
└── Documentation/
    ├── DATA_PARTITIONING_STRATEGY.md
    └── PARTITION_STATUS.md
```

### Database Schema (Consistent Across All Partitions)
```sql
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

-- Performance indexes
CREATE INDEX idx_symbol_timestamp ON market_bars(symbol, timestamp);
CREATE INDEX idx_timestamp ON market_bars(timestamp);
CREATE INDEX idx_date_only ON market_bars(date_only);
CREATE INDEX idx_symbol_date ON market_bars(symbol, date_only);
```

## 🎯 Strategic Benefits

### 1. **Version Control Efficiency**
- **40MB files** are manageable for Git operations
- **Individual partitions** can be versioned independently
- **Selective updates** without affecting entire dataset
- **GitHub compatibility** with reasonable file sizes

### 2. **Market Regime Coverage**
- **2021-2025**: COVID recovery, inflation cycle, current markets
- **2016-2020**: Bull market, COVID crash, early recovery
- **2011-2015**: Post-financial crisis recovery, QE era
- **2006-2010**: Financial crisis, market crash, early recovery
- **2001-2005**: Dot-com crash recovery, early 2000s

### 3. **Performance Optimization**
- **Faster queries** on relevant time periods
- **Reduced memory usage** for specific backtests
- **Parallel processing** across multiple partitions
- **Scalable architecture** for future extensions

## 🔄 Partition Management

### Current Status (spy_2021_2025.db)
- **Data Range**: April 2021 - July 2025 (48 months acquired)
- **Missing**: Jan-Mar 2021, May-Aug 2021, Aug-Dec 2025
- **File Size**: 35MB (room for ~60 more months)
- **Next Steps**: Complete 2021 and acquire 2025 as available

### Acquisition Priority
1. **Complete 2021-2025 partition** (top priority)
   - Acquire remaining 2021 months (Jan-Mar, May-Aug)
   - Add 2025 months as they become available

2. **Build 2016-2020 partition** (high priority)
   - Covers pre-COVID bull market and crash
   - Essential for comprehensive strategy validation

3. **Historical partitions** (medium priority)
   - 2011-2015: Post-crisis recovery patterns
   - 2006-2010: Financial crisis for stress testing
   - 2001-2005: Early market regime comparison

## 🛠️ Implementation Tools

### Partition Management Scripts
- **`create_partition.ps1`** - Initialize new partition database
- **`migrate_to_partition.ps1`** - Move data between partitions
- **`verify_partition.ps1`** - Integrity check for partition
- **`merge_partitions.ps1`** - Combine partitions for analysis

### Query Abstraction
```csharp
public class PartitionedDataProvider
{
    private readonly Dictionary<string, string> _partitionPaths;
    
    public async Task<List<MarketBar>> GetBarsAsync(DateTime start, DateTime end)
    {
        var relevantPartitions = GetPartitionsForDateRange(start, end);
        var allBars = new List<MarketBar>();
        
        foreach (var partition in relevantPartitions)
        {
            var bars = await LoadBarsFromPartition(partition, start, end);
            allBars.AddRange(bars);
        }
        
        return allBars.OrderBy(b => b.Timestamp).ToList();
    }
}
```

## 📈 Growth Strategy

### Automatic Partitioning
- **Size threshold**: When partition exceeds 45MB, consider split
- **Time boundary**: Maintain clean 5-year boundaries
- **Migration process**: Seamless data movement between partitions

### Future Extensions
- **Symbol expansion**: Add QQQ, IWM partitions using same scheme
- **Resolution variants**: Consider 1-minute partitions for recent years
- **International markets**: Apply same partitioning to international data

## 🔍 Quality Assurance

### Partition Integrity Checks
1. **Completeness**: No missing trading days within partition period
2. **Consistency**: Schema and indexes identical across partitions  
3. **Performance**: Query response times within acceptable limits
4. **Size validation**: File size within expected range (35-45MB)

### Cross-Partition Validation
- **Date continuity**: No gaps between partition boundaries
- **Data integrity**: OHLCV validation across all partitions
- **Performance testing**: Consistent query performance

## 📝 Maintenance Schedule

### Monthly
- **Update current partition** with latest data
- **Verify partition integrity** and performance
- **Monitor file sizes** and plan splits if needed

### Quarterly  
- **Cross-partition validation** for data continuity
- **Performance benchmarking** across all partitions
- **GitHub repository optimization** and cleanup

### Annually
- **Archive strategy review** for older partitions
- **Performance optimization** updates
- **Schema evolution** planning

---

**This partitioning strategy ensures scalable, maintainable, and high-performance access to 20+ years of SPY market data while keeping individual files at GitHub-friendly sizes.**