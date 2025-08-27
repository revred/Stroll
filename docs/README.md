# Stroll Documentation

This directory contains consolidated documentation for the Stroll project.

## Overview

Stroll is a high-performance options backtesting system with comprehensive data management capabilities.

## Documentation Structure

### Core Documentation
- [README.md](../README.md) - Main project overview and quick start guide
- [CODING_GUIDELINES.md](CODING_GUIDELINES.md) - Development standards and conventions
- [MCP_SERVICE_TEST_RESULTS.md](MCP_SERVICE_TEST_RESULTS.md) - MCP integration test results
- [FastReport.md](FastReport.md) - Performance reporting documentation

### Module-Specific Documentation

#### Stroll.History
- Data acquisition and historical storage system
- Location: `Stroll.History/docs/`

#### Stroll.Runner  
- Backtesting execution engine and test suites
- Location: `Stroll.Runner/*/README.md`

#### Stroll.Runtime
- Runtime services and process management
- Location: `Stroll.Runtime/*/README.md`

#### Stroll.Strategy
- Trading strategy components and signals
- Location: `Stroll.Strategy/docs/` (if applicable)

### Optional Components

#### Stroll.Polygon.IO
**Note**: This is a Git subproject located in `Stroll.History/Stroll.Polygon.IO/` and is only needed for:
- New data acquisition from Polygon.io
- Data ingestion tasks
- Expanding historical coverage

For Polygon.io documentation, see: `Stroll.History/Stroll.Polygon.IO/README.md`

## Getting Started

1. See the main [README.md](../README.md) for installation and quick start
2. Review [CODING_GUIDELINES.md](CODING_GUIDELINES.md) for development standards
3. Check module-specific documentation for detailed implementation guides

## Architecture

The Stroll system follows a modular architecture:

```
Stroll/
├── docs/              # This documentation
├── scripts/           # Build and utility scripts
├── tools/             # Development tools and utilities
├── Stroll.History/    # Historical data system
├── Stroll.Runner/     # Backtesting engine
├── Stroll.Runtime/    # Runtime services
└── Stroll.Strategy/   # Trading strategies (future)
```

For detailed architecture documentation, see individual module README files.