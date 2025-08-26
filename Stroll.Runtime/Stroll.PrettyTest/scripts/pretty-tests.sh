#!/usr/bin/env bash
set -euo pipefail
dotnet run --project Stroll.PrettyTest -- "$@"
