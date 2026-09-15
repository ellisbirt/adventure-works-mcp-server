#!/usr/bin/env bash
set -euo pipefail

readonly project_path="src/gateway/Gateway.csproj"
readonly context_name="AdventureWorksDbContext"
readonly context_directory="Data/Scaffolded"
readonly output_directory="Data/Scaffolded/Entities"
readonly entity_namespace="EnterpriseAiGateway.Data.Scaffolded.Entities"
readonly context_namespace="EnterpriseAiGateway.Data.Scaffolded"

if [[ -z "${ADVENTURE_WORKS_CONNECTION_STRING:-}" ]]; then
    echo "ADVENTURE_WORKS_CONNECTION_STRING must be set." >&2
    exit 1
fi

if ! command -v dotnet-ef >/dev/null 2>&1; then
    echo "dotnet-ef is required. Install it with: dotnet tool install --global dotnet-ef --version 8.0.11" >&2
    exit 1
fi

dotnet-ef dbcontext scaffold "$ADVENTURE_WORKS_CONNECTION_STRING" Microsoft.EntityFrameworkCore.SqlServer \
    --project "$project_path" \
    --context "$context_name" \
    --context-dir "$context_directory" \
    --output-dir "$output_directory" \
    --schema SalesLT \
    --namespace "$entity_namespace" \
    --context-namespace "$context_namespace" \
    --no-onconfiguring \
    --force