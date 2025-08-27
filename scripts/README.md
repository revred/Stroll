# Stroll Scripts

This directory contains build, deployment, and utility scripts for the Stroll project.

## Scripts

### Git and Development
- `pre-commit.sh` / `pre-commit.ps1` - Pre-commit hooks for code quality
- `demo-clean-mcp.sh` - MCP service cleanup utility

## Module-Specific Scripts

### Stroll.History Scripts
Location: `Stroll.History/scripts/`

- Data acquisition scripts
- Database maintenance utilities
- Partition management tools

### Stroll.Runtime Scripts  
Location: `Stroll.Runtime/*/scripts/`

- Test execution scripts
- Service management utilities

## Usage

Most scripts are designed to be run from the project root directory:

```bash
# Linux/macOS
./scripts/pre-commit.sh

# Windows PowerShell
.\scripts\pre-commit.ps1
```

For module-specific scripts, see the respective module documentation.