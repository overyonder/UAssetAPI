#!/usr/bin/env bash
# dataset.sh <tool dir> <package list> <root> <out dir>: the --dataset pass over a package list within one cap of 2 cores and
# 4 GB at nice 19. Each uasset-dump process is single-threaded, so two run at once, each on 500-package batches with a 1.75 GB
# heap; packages that run out of heap are then retried one process at a time with 3 GB. A finished batch is skipped on a
# rerun, so an interrupted pass resumes. Writes <out dir>/<path under root>.txt and .native.json (payloads over 64 KiB as
# .payloads/<SHA-1>.bin), index.tsv (package, text, native, exports, residues) and residue.tsv (the --audit and fail lines).
set -u
tool=$1 list=$2 root=$3 out=$4
if [ ! -f "$list" ] || [ ! -r "$list" ]; then
  printf '# WARNING: No complete coverage can be claimed based on this output.\n# input package list could not be read: %s\n' "$list" >&2
  exit 1
fi
M=$(dirname "$(readlink -f "$0")")/mappings
usmaps=${UASSET_USMAP_DIR:-/mnt/bulk/games/asset-packs/unreal/sample-projects/asset-dump/mappings}
export UASSET_ENGINE=~/code/github.com/EpicGames/UnrealEngine/Engine
export UASSET_OODLE=$UASSET_ENGINE/Binaries/DotNET/UnrealBuildTool/runtimes/linux-x64/native/liboo2core.so
export UASSET_PARENTS=$M/project-parents.tsv UASSET_MAPS=$M/project-maps.tsv:$M/removed-maps.tsv UASSET_MOUNTS=$M/project-plugins.lst
export UASSET_USMAP=$(ls "$usmaps"/*.usmap | paste -sd:)
export tool root out dotnet=/nix/store/pkng7qyg6mxf77lrqlaal9s6igy35n4x-dotnet-sdk-wrapped-10.0.401/bin/dotnet
mkdir -p "$out/batches"
[ -e "$out/batches/b.00000" ] || split -l 500 -d -a 5 "$list" "$out/batches/b."
export total=$(ls "$out"/batches | grep -c '^b\.[0-9]*$')
batch() {
  [ -e "$1.status" ] && return
  DOTNET_GCHeapHardLimit=$2 timeout 7200 xargs -d '\n' "$dotnet" "$tool/uasset-dump.dll" --dataset "$root" "$out" < "$1" > "$1.out" 2> "$1.err"
  echo $? > "$1.status"
  echo "$(ls "$out"/batches/*.status | wc -l)/$total $(date +%T)" > "$out/progress"
}
export -f batch
systemd-run --user --quiet --scope --collect -p MemoryMax=4G -p CPUQuota=200% nice -n 19 bash -c '
  ls "$out"/batches/b.[0-9][0-9][0-9][0-9][0-9] | xargs -P 2 -I{} bash -c "batch {} 0x70000000"
  cat "$out"/batches/b.[0-9]*.out | awk -F"\t" "(\$1==\"fail\" && \$2 ~ /OutOfMemory/) || (\$1==\"rawstruct\" && \$4 ~ /OutOfMemoryException/) {print \$NF}" | sort -u > "$out/batches/retry"
  if [ -s "$out/batches/retry" ]; then split -l 50 -d -a 3 "$out/batches/retry" "$out/batches/r."; for b in "$out"/batches/r.[0-9][0-9][0-9]; do batch "$b" 0xC0000000; done; fi'
# a retried package's out-of-memory fail line gives way to its retry's lines
cat "$out"/batches/r.[0-9]*.out 2>/dev/null > "$out/batches/retried.out"
awk -F'\t' -v retried="$out/batches/retried.out" 'FILENAME==retried { if ($1=="index") done[$2]=1; next } !(($1=="index" && ($2 in done)) || ($NF in done))' \
  "$out/batches/retried.out" "$out"/batches/b.[0-9]*.out > "$out/batches/all.out"
cat "$out/batches/retried.out" >> "$out/batches/all.out"
"$dotnet" "$tool/uasset-dump.dll" --merge-diagnostics --records "$out/batches/all.out" > "$out/batches/merged.out" 2> "$out/batches/merge.err" || {
  cat "$out/batches/merge.err" >&2
  exit 1
}
mv "$out/batches/merged.out" "$out/batches/all.out"
grep '^index' "$out/batches/all.out" | cut -f2- > "$out/index.tsv"
grep -v '^index' "$out/batches/all.out" > "$out/residue.tsv"
"$dotnet" "$tool/uasset-dump.dll" --coverage-summary "$out/index.tsv" "$out" "$list"
echo "batches $(ls "$out"/batches/*.status | wc -l), nonzero $(grep -L '^0$' "$out"/batches/*.status | wc -l), packages $(wc -l < "$out/index.tsv"), residue lines $(wc -l < "$out/residue.tsv")" | tee "$out/summary"
