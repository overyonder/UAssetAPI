#!/usr/bin/env bash
# generate-project-parents.sh <header list> > project-parents.tsv
# Reflected class name and reflected parent name for every UCLASS declared in the listed project and plugin headers,
# which no engine table or mappings describe. A declaration may carry an export macro or UE_DEPRECATED(...) before the
# name and may break the line before ": public". The list comes from `find` on local checkouts and `nas find` on the NAS.
set -euo pipefail
xargs -d '\n' rg --no-config --no-heading --no-filename -U -P -o \
  -e '(?:UCLASS|UINTERFACE)\s*\((?:[^()]|\([^()]*\))*\)\s*class\s+(?:\w+(?:\([^)]*\))?\s+)*?[UA](\w+)\s*(?:final\s*)?:\s*public\s+(?:\w+::)*[UA](\w+)' \
  -r '$1	$2' < "$1" | awk -F'\t' -v OFS='\t' '{for (i = 1; i <= 2; i++) sub(/^DEPRECATED_/, "", $i)} $1 != $2' | sort -u
