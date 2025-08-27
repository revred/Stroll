#!/bin/bash
# Stroll Project Pre-Commit Verification Script
# Ensures zero build errors, zero warnings, and all tests pass

set -e  # Exit on any error

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

PROJECT_PATH=${1:-.}
SKIP_PERFORMANCE=${2:-false}

echo -e "${GREEN}🔍 STROLL PRE-COMMIT VERIFICATION${NC}"
echo -e "${GREEN}==================================${NC}"

BUILD_FAILED=false
TESTS_FAILED=false
WARNINGS_FOUND=false

echo -e "${CYAN}📂 Working Directory: $(pwd)${NC}"
echo ""

# 1. Clean build
echo -e "${YELLOW}🧹 Cleaning solution...${NC}"
if ! dotnet clean "$PROJECT_PATH" --verbosity quiet > /dev/null 2>&1; then
    echo -e "${RED}❌ Clean failed${NC}"
    exit 1
fi
echo -e "${GREEN}✅ Solution cleaned${NC}"

# 2. Restore packages  
echo -e "${YELLOW}📦 Restoring packages...${NC}"
if ! dotnet restore "$PROJECT_PATH" --verbosity quiet > /dev/null 2>&1; then
    echo -e "${RED}❌ Package restore failed${NC}"
    exit 1
fi
echo -e "${GREEN}✅ Packages restored${NC}"

# 3. Build with strict warning checks
echo -e "${YELLOW}🏗️ Building solution with warning detection...${NC}"
BUILD_OUTPUT=$(dotnet build "$PROJECT_PATH" --no-restore --verbosity normal 2>&1)
BUILD_EXIT_CODE=$?

# Check build success
if [ $BUILD_EXIT_CODE -ne 0 ]; then
    echo -e "${RED}❌ BUILD FAILED${NC}"
    echo "$BUILD_OUTPUT"
    BUILD_FAILED=true
fi

# Parse warnings and errors
WARNINGS=$(echo "$BUILD_OUTPUT" | grep -i "warning\s\+\(CS\|CA\|IDE\)[0-9]" || true)
ERRORS=$(echo "$BUILD_OUTPUT" | grep -i "error\s\+\(CS\|CA\)[0-9]" || true)

# Report errors
if [ -n "$ERRORS" ]; then
    echo -e "${RED}❌ BUILD ERRORS DETECTED:${NC}"
    echo "$ERRORS" | while read -r line; do
        echo -e "${RED}   $line${NC}"
    done
    BUILD_FAILED=true
fi

# Report warnings - ZERO TOLERANCE
if [ -n "$WARNINGS" ]; then
    echo -e "${RED}❌ BUILD WARNINGS DETECTED (NOT ALLOWED):${NC}"
    echo "$WARNINGS" | while read -r line; do
        echo -e "${RED}   $line${NC}"
    done
    echo ""
    echo -e "${RED}🚫 ZERO WARNINGS POLICY: Fix all warnings before committing${NC}"
    WARNINGS_FOUND=true
    BUILD_FAILED=true
fi

if [ "$BUILD_FAILED" = false ]; then
    echo -e "${GREEN}✅ Build successful - ZERO warnings, ZERO errors${NC}"
fi

# 4. Run all tests if build succeeded
if [ "$BUILD_FAILED" = false ]; then
    echo -e "${YELLOW}🧪 Running all tests...${NC}"
    
    if ! TEST_OUTPUT=$(dotnet test "$PROJECT_PATH" --no-build --verbosity normal --logger "console;verbosity=detailed" 2>&1); then
        echo -e "${RED}❌ TESTS FAILED${NC}"
        echo "$TEST_OUTPUT"
        TESTS_FAILED=true
    else
        echo -e "${GREEN}✅ ALL TESTS PASSED${NC}"
        # Show test summary
        echo "$TEST_OUTPUT" | grep -E "(Passed!|Total tests:)" | while read -r line; do
            echo -e "${GREEN}   $line${NC}"
        done
    fi
fi

# 5. Performance regression check (optional)
if [ "$BUILD_FAILED" = false ] && [ "$TESTS_FAILED" = false ] && [ "$SKIP_PERFORMANCE" != "true" ]; then
    echo -e "${YELLOW}⚡ Running performance tests...${NC}"
    
    if ! PERF_OUTPUT=$(dotnet test "$PROJECT_PATH" --filter "Category=Performance" --no-build --verbosity minimal 2>&1); then
        echo -e "${YELLOW}⚠️ PERFORMANCE TESTS FAILED${NC}"
        echo -e "${YELLOW}   Check for performance regressions${NC}"
        echo "$PERF_OUTPUT"
        # Don't fail commit for performance issues, just warn
    else
        echo -e "${GREEN}✅ Performance tests passed${NC}"
    fi
fi

# 6. Additional project-specific checks
echo -e "${YELLOW}🔍 Running project-specific checks...${NC}"

# Check for data integrity tests
if [ -d "Stroll.Runner/Stroll.History.Integrity.Tests" ]; then
    if ! INTEGRITY_OUTPUT=$(dotnet test "Stroll.Runner/Stroll.History.Integrity.Tests" --no-build --verbosity minimal 2>&1); then
        echo -e "${YELLOW}⚠️ Data integrity tests failed${NC}"
        echo "$INTEGRITY_OUTPUT"
    else
        echo -e "${GREEN}✅ Data integrity tests passed${NC}"
    fi
fi

# Final verdict
echo ""
echo -e "${CYAN}📊 FINAL RESULTS:${NC}"
echo -e "${CYAN}=================${NC}"

if [ "$BUILD_FAILED" = true ]; then
    echo -e "${RED}❌ BUILD: FAILED${NC}"
else
    echo -e "${GREEN}✅ BUILD: PASSED (zero warnings, zero errors)${NC}"
fi

if [ "$TESTS_FAILED" = true ]; then
    echo -e "${RED}❌ TESTS: FAILED${NC}"
else
    echo -e "${GREEN}✅ TESTS: PASSED (all tests successful)${NC}"
fi

if [ "$BUILD_FAILED" = true ] || [ "$TESTS_FAILED" = true ]; then
    echo ""
    echo -e "${RED}🚫 COMMIT REJECTED${NC}"
    echo -e "${RED}================================${NC}"
    echo -e "${RED}Fix all issues above before committing:${NC}"
    
    if [ "$WARNINGS_FOUND" = true ]; then
        echo -e "${RED}• Eliminate ALL compiler warnings${NC}"
    fi
    if [ "$BUILD_FAILED" = true ] && [ "$WARNINGS_FOUND" = false ]; then
        echo -e "${RED}• Fix all build errors${NC}"
    fi
    if [ "$TESTS_FAILED" = true ]; then
        echo -e "${RED}• Ensure ALL tests pass${NC}"
    fi
    
    echo ""
    echo -e "${YELLOW}Re-run this script after fixes: ./pre-commit.sh${NC}"
    exit 1
else
    echo ""
    echo -e "${GREEN}🎉 COMMIT APPROVED${NC}"
    echo -e "${GREEN}===============================${NC}"
    echo -e "${GREEN}✅ Zero build errors${NC}"
    echo -e "${GREEN}✅ Zero build warnings${NC}"
    echo -e "${GREEN}✅ All tests pass${NC}"
    echo -e "${GREEN}✅ Code quality verified${NC}"
    echo ""
    echo -e "${GREEN}Ready to commit! 🚀${NC}"
    exit 0
fi