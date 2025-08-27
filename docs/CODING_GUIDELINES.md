# Stroll Project - Coding Guidelines

## 🎯 Build Quality Standards

### ✅ Zero Tolerance Policy
- **NO BUILD ERRORS** - All projects must compile successfully
- **NO WARNINGS** - Code must be warning-free
- **ALL TESTS PASS** - 100% test success rate required before check-in

### 🏗️ Build Requirements

#### Pre-Check-in Checklist
```bash
# 1. Clean and rebuild all projects
dotnet clean
dotnet build --no-restore --verbosity normal

# 2. Verify zero warnings
dotnet build --no-restore --verbosity normal | grep -i warning
# Expected: No output (empty result)

# 3. Run all tests
dotnet test --no-build --verbosity normal

# 4. Performance tests (if applicable)
dotnet test --filter "Category=Performance" --no-build
```

#### Build Commands by Project Type

**Core Projects:**
```bash
# Historical data system
cd Stroll.History/Stroll.Historical
dotnet build --configuration Release --no-restore

# Backtest engine
cd Stroll.Runner/Stroll.Backtest.Tests  
dotnet build --configuration Release --no-restore

# Strategy components
cd Stroll.Strategy
dotnet build --configuration Release --no-restore
```

**Solution-wide Build:**
```bash
# Build entire solution
dotnet build Stroll.sln --configuration Release --no-restore --verbosity normal

# Verify no warnings across solution
dotnet build Stroll.sln --verbosity normal 2>&1 | grep -E "(Warning|Error)"
# Expected: No output
```

## 🧪 Testing Standards

### Mandatory Test Categories

#### 1. Unit Tests
```bash
# All unit tests must pass
dotnet test --filter "Category=Unit" --logger "console;verbosity=detailed"
```

#### 2. Integration Tests
```bash
# Database and API integration tests
dotnet test --filter "Category=Integration" --logger "console;verbosity=detailed"
```

#### 3. Performance Tests
```bash
# Performance regression prevention
dotnet test --filter "Category=Performance" --logger "console;verbosity=detailed"
```

#### 4. Data Integrity Tests
```bash
# Historical data validation
cd Stroll.Runner/Stroll.History.Integrity.Tests
dotnet test --logger "console;verbosity=detailed"
```

### Test Quality Requirements
- **Coverage**: Minimum 80% code coverage for new code
- **Performance**: No regression in benchmark tests
- **Reliability**: Tests must be deterministic (no flaky tests)
- **Speed**: Unit test suite must complete within 30 seconds

## 🔧 Code Quality Standards

### Compiler Warnings
```xml
<!-- All projects must include in .csproj -->
<PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <WarningsNotAsErrors />
</PropertyGroup>
```

### Nullable Reference Types
```csharp
// Enable in all new projects
#nullable enable

// Example of proper nullable handling
public class DataProvider
{
    public string? ApiKey { get; set; }
    
    public async Task<HistoricalData> GetDataAsync(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        
        if (ApiKey is null)
            throw new InvalidOperationException("API key required");
            
        return await FetchDataAsync(symbol);
    }
}
```

### Error Handling
```csharp
// Proper exception handling pattern
public async Task<BacktestResult> RunBacktestAsync()
{
    try
    {
        var data = await LoadHistoricalDataAsync();
        return await ExecuteBacktestAsync(data);
    }
    catch (DataNotFoundException ex)
    {
        _logger.LogError(ex, "Historical data not found");
        throw new BacktestException("Cannot run backtest without historical data", ex);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Unexpected error during backtest");
        throw;
    }
}
```

## 📋 Pre-Commit Verification Script

### PowerShell Pre-Commit Hook
```powershell
# pre-commit.ps1
param(
    [string]$ProjectPath = "."
)

Write-Host "🔍 PRE-COMMIT VERIFICATION" -ForegroundColor Green
Write-Host "=========================" -ForegroundColor Green

$ErrorActionPreference = "Stop"
$buildFailed = $false
$testsFailed = $false

try {
    # 1. Clean build
    Write-Host "🧹 Cleaning solution..." -ForegroundColor Yellow
    dotnet clean $ProjectPath --verbosity quiet
    
    # 2. Restore packages
    Write-Host "📦 Restoring packages..." -ForegroundColor Yellow  
    dotnet restore $ProjectPath --verbosity quiet
    
    # 3. Build with warning checks
    Write-Host "🏗️ Building solution..." -ForegroundColor Yellow
    $buildOutput = dotnet build $ProjectPath --no-restore --verbosity normal 2>&1
    
    # Check for warnings
    $warnings = $buildOutput | Where-Object { $_ -match "warning" }
    if ($warnings) {
        Write-Host "❌ BUILD WARNINGS DETECTED:" -ForegroundColor Red
        $warnings | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
        $buildFailed = $true
    }
    
    # Check for errors
    $errors = $buildOutput | Where-Object { $_ -match "error" }
    if ($errors) {
        Write-Host "❌ BUILD ERRORS DETECTED:" -ForegroundColor Red
        $errors | ForEach-Object { Write-Host "   $_" -ForegroundColor Red }
        $buildFailed = $true
    }
    
    if (!$buildFailed) {
        Write-Host "✅ Build successful - no warnings or errors" -ForegroundColor Green
    }
    
    # 4. Run tests
    Write-Host "🧪 Running tests..." -ForegroundColor Yellow
    $testOutput = dotnet test $ProjectPath --no-build --verbosity normal --logger "console;verbosity=minimal"
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ TESTS FAILED" -ForegroundColor Red
        $testsFailed = $true
    } else {
        Write-Host "✅ All tests passed" -ForegroundColor Green
    }
    
    # 5. Performance regression check
    Write-Host "⚡ Checking performance..." -ForegroundColor Yellow
    $perfOutput = dotnet test $ProjectPath --filter "Category=Performance" --no-build --verbosity quiet
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "⚠️ Performance tests failed - check for regressions" -ForegroundColor Yellow
    } else {
        Write-Host "✅ Performance tests passed" -ForegroundColor Green
    }
    
} catch {
    Write-Host "❌ PRE-COMMIT CHECK FAILED: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

# Final verdict
if ($buildFailed -or $testsFailed) {
    Write-Host ""
    Write-Host "❌ COMMIT REJECTED" -ForegroundColor Red
    Write-Host "Fix all issues before committing" -ForegroundColor Red
    exit 1
} else {
    Write-Host ""
    Write-Host "✅ READY TO COMMIT" -ForegroundColor Green
    Write-Host "All quality checks passed" -ForegroundColor Green
    exit 0
}
```

### Bash Pre-Commit Hook
```bash
#!/bin/bash
# pre-commit.sh

echo "🔍 PRE-COMMIT VERIFICATION"
echo "========================="

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Exit on any error
set -e

# 1. Clean and restore
echo -e "${YELLOW}🧹 Cleaning and restoring...${NC}"
dotnet clean --verbosity quiet
dotnet restore --verbosity quiet

# 2. Build with warning detection
echo -e "${YELLOW}🏗️ Building solution...${NC}"
BUILD_OUTPUT=$(dotnet build --no-restore --verbosity normal 2>&1)

# Check for warnings
if echo "$BUILD_OUTPUT" | grep -i "warning" > /dev/null; then
    echo -e "${RED}❌ BUILD WARNINGS DETECTED:${NC}"
    echo "$BUILD_OUTPUT" | grep -i "warning"
    exit 1
fi

# Check for errors
if echo "$BUILD_OUTPUT" | grep -i "error" > /dev/null; then
    echo -e "${RED}❌ BUILD ERRORS DETECTED:${NC}"
    echo "$BUILD_OUTPUT" | grep -i "error"
    exit 1
fi

echo -e "${GREEN}✅ Build successful - no warnings or errors${NC}"

# 3. Run all tests
echo -e "${YELLOW}🧪 Running tests...${NC}"
if ! dotnet test --no-build --verbosity normal; then
    echo -e "${RED}❌ TESTS FAILED${NC}"
    exit 1
fi

echo -e "${GREEN}✅ All tests passed${NC}"

# 4. Performance check
echo -e "${YELLOW}⚡ Checking performance...${NC}"
if ! dotnet test --filter "Category=Performance" --no-build --verbosity quiet; then
    echo -e "${YELLOW}⚠️ Performance tests failed - check for regressions${NC}"
fi

echo -e "${GREEN}✅ READY TO COMMIT${NC}"
echo "All quality checks passed"
```

## 🚨 Common Issues and Solutions

### Build Warnings
```csharp
// ❌ Avoid - generates CS8618 warning
public class Strategy
{
    public string Name { get; set; }
}

// ✅ Correct - no warnings
public class Strategy
{
    public required string Name { get; set; }
    // OR
    public string Name { get; set; } = string.Empty;
    // OR
    public string? Name { get; set; }
}
```

### Async/Await Patterns
```csharp
// ❌ Avoid - CS4014 warning (fire and forget)
Task.Run(() => ProcessDataAsync());

// ✅ Correct - proper async handling
await Task.Run(() => ProcessDataAsync());

// OR for background processing
_ = Task.Run(async () => {
    try 
    {
        await ProcessDataAsync();
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Background processing failed");
    }
});
```

### Using Statements
```csharp
// ❌ Avoid - potential resource leaks
var connection = new SqlConnection(connectionString);

// ✅ Correct - proper disposal
using var connection = new SqlConnection(connectionString);
// OR
using (var connection = new SqlConnection(connectionString))
{
    // Use connection
}
```

## 🎨 Documentation Formatting

### Unicode Icons
Always include a space after Unicode icons for better readability in both console and documentation:

**✅ Correct:**
```markdown
## 🚀 Performance Achievements
- ✅ Build successful
- ❌ Test failed
```

**❌ Incorrect:**
```markdown  
## 🚀Performance Achievements
- ✅Build successful
- ❌Test failed
```

### Consistent Icon Usage
- Use meaningful icons that enhance readability
- Maintain consistency across related documentation
- Prefer widely supported Unicode characters

## 📖 IDE Configuration

### Visual Studio Settings
```json
// .editorconfig
root = true

[*.cs]
dotnet_analyzer_diagnostic.severity = warning
dotnet_code_quality_unused_parameters = all:warning
dotnet_style_require_accessibility_modifiers = always:warning

[*.{cs,vb}]
dotnet_diagnostic.CA1303.severity = none
dotnet_diagnostic.IDE0058.severity = none
```

### VS Code Settings
```json
// .vscode/settings.json
{
    "dotnet.completion.showCompletionItemsFromUnimportedNamespaces": true,
    "omnisharp.enableRoslynAnalyzers": true,
    "dotnet.server.useOmnisharp": false,
    "editor.formatOnSave": true,
    "csharp.format.enable": true
}
```

## 🎯 Team Workflow

### Daily Development
1. **Start of day**: Pull latest changes, verify build
2. **During development**: Run `dotnet build` frequently  
3. **Before lunch/end of day**: Run full test suite
4. **Before commit**: Execute pre-commit script

### Code Review Requirements
- ✅ All CI/CD checks must pass
- ✅ Code coverage maintained or improved
- ✅ Performance benchmarks stable
- ✅ No new warnings introduced
- ✅ Tests added for new functionality

### Git Hooks Setup
```bash
# Install pre-commit hook
cp pre-commit.sh .git/hooks/pre-commit
chmod +x .git/hooks/pre-commit

# Install pre-push hook for final verification
cp pre-commit.sh .git/hooks/pre-push  
chmod +x .git/hooks/pre-push
```

---

## 📝 Summary

**ZERO TOLERANCE POLICY:**
- 🚫 No build errors
- 🚫 No build warnings  
- 🚫 No failing tests
- 🚫 No performance regressions

**MANDATORY BEFORE EACH COMMIT:**
1. Clean build of entire solution
2. Zero warnings verification
3. All tests pass (unit, integration, performance)
4. Code quality checks

**USE THE PRE-COMMIT SCRIPTS** - They automate these checks and prevent bad commits from reaching the repository.