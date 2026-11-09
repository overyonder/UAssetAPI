#!/usr/bin/env bash
# full-audit.sh <tool dir> <package list> <out dir>: one capped process at a time (2 cores, 4 GB, nice 19), 50 packages
# per batch; a batch that finished is skipped on a rerun, so an interrupted audit resumes
set -u
tool=$1 list=$2 out=$3
M=$(dirname "$(readlink -f "$0")")/mappings
usmaps=${UASSET_USMAP_DIR:-/mnt/bulk/games/asset-packs/unreal/sample-projects/asset-dump/mappings}
export UASSET_ENGINE=~/code/github.com/EpicGames/UnrealEngine/Engine UASSET_OODLE=~/code/github.com/EpicGames/UnrealEngine/Engine/Binaries/DotNET/UnrealBuildTool/runtimes/linux-x64/native/liboo2core.so
export UASSET_PARENTS=$M/project-parents.tsv UASSET_USMAP=$(ls "$usmaps"/*.usmap | paste -sd:)
export UASSET_MAPS=$M/project-maps.tsv:$M/removed-maps.tsv UASSET_MOUNTS=$M/project-plugins.lst
mkdir -p "$out"
[ -e "$out/batch.0000" ] || split -l 50 -d -a 4 "$list" "$out/batch."
for b in "$out"/batch.[0-9][0-9][0-9][0-9]; do
  [ -e "$b.status" ] && continue
  systemd-run --user --quiet --scope --collect -p MemoryMax=4G -p CPUQuota=200% nice -n 19 \
    timeout 1800 xargs -d '\n' /nix/store/pkng7qyg6mxf77lrqlaal9s6igy35n4x-dotnet-sdk-wrapped-10.0.401/bin/dotnet "$tool/uasset-dump.dll" --audit < "$b" > "$b.tsv" 2> "$b.err"
  echo $? > "$b.status"
done
/nix/store/pkng7qyg6mxf77lrqlaal9s6igy35n4x-dotnet-sdk-wrapped-10.0.401/bin/dotnet "$tool/uasset-dump.dll" --merge-diagnostics "$out"/batch.*.tsv > "$out/audit.tsv"
echo "batches $(ls "$out"/*.status | wc -l), nonzero $(grep -L '^0$' "$out"/*.status | wc -l)"
