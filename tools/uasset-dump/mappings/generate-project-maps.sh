#!/usr/bin/env bash
# generate-project-maps.sh <header list> > project-maps.tsv
# Owning class or struct, property name, key struct and value struct for every TMap member of a type declared in the
# listed project and plugin headers whose key or value is a struct. Packages saved before PROPERTY_TAG_COMPLETE_TYPE_NAME
# don't name a map's struct types in its tag, and no mappings describe project types. A column is empty when that side
# isn't a struct. Owners are reflected names, without the U, A or F prefix.
set -euo pipefail
xargs -d '\n' awk -v OFS='\t' '
  function s(t) { return (t ~ /^F[A-Z]/ && t !~ /^(FName|FString|FText)$/) ? substr(t, 2) : "" }
  FNR == 1 { owner = "" }
  { sub(/\r$/, "") }
  match($0, /^[ \t]*(class|struct)[ \t]+([A-Z0-9_]+_API[ \t]+)?[UAF][A-Za-z0-9_]+[ \t]*(final[ \t]*)?(:|$)/) {
    n = split($0, w, /[ \t:]+/); for (i = 1; i <= n; i++) if (w[i] == "class" || w[i] == "struct") break
    i++; if (w[i] ~ /_API$/) i++; owner = substr(w[i], 2)
  }
  match($0, /TMap[ \t]*<[ \t]*[A-Za-z0-9_:]+[ \t]*(<[^<>]*>)?[ \t]*,[ \t]*[A-Za-z0-9_:]+[ \t]*(<[^<>]*>)?[ \t]*>[ \t]*[A-Za-z0-9_]+[ \t]*[;=]/) {
    d = substr($0, RSTART + 4, RLENGTH - 4); sub(/^[ \t]*</, "", d)
    gsub(/<[^<>]*>/, "", d)  # template arguments of the key or value type
    split(d, p, /[ \t]*[,>][ \t]*/); name = p[3]; sub(/[ \t]*[;=].*/, "", name)
    gsub(/[ \t]/, "", p[1]); gsub(/[ \t]/, "", p[2])
    k = s(p[1]); v = s(p[2]); if (owner != "" && (k != "" || v != "")) print owner, name, k, v
  }' < "$1" | sort -u
