#!/usr/bin/env bash
# dataset.sh <tool dir> <package list> <root> <out dir>: the --dataset pass over a package list, one capped process at a
# time (2 cores, 4 GB, nice 19), 200 packages per batch. A finished batch is skipped on a rerun, so an interrupted pass
# resumes. Writes <out dir>/<path under root>.txt and .native.json, index.tsv (package, text, native, exports, residues)
# and residue.tsv (the --audit lines).
set -u
tool=$1 list=$2 root=$3 out=$4
M=$(dirname "$(readlink -f "$0")")/mappings
export UASSET_ENGINE=~/.local/opt/unreal-engine/5.8.3/Engine
export UASSET_OODLE=$UASSET_ENGINE/Binaries/DotNET/AutomationTool/AutomationScripts/BuildGraph/net10.0/runtimes/linux-x64/native/liboo2core.so
export UASSET_PARENTS=$M/project-parents.tsv UASSET_MAPS=$M/project-maps.tsv:$M/removed-maps.tsv UASSET_MOUNTS=$M/project-plugins.lst
export UASSET_USMAP=$(ls "$M"/*.usmap | paste -sd:) DOTNET_GCHeapHardLimit=0xC0000000
dotnet=/nix/store/pkng7qyg6mxf77lrqlaal9s6igy35n4x-dotnet-sdk-wrapped-10.0.401/bin/dotnet
mkdir -p "$out/batches"
[ -e "$out/batches/b.00000" ] || split -l 200 -d -a 5 "$list" "$out/batches/b."
total=$(ls "$out"/batches/b.[0-9]* | grep -c '/b\.[0-9]*$')
for b in "$out"/batches/b.[0-9][0-9][0-9][0-9][0-9]; do
  [ -e "$b.status" ] && continue
  systemd-run --user --quiet --scope --collect -p MemoryMax=4G -p CPUQuota=200% nice -n 19 \
    timeout 3600 xargs -d '\n' "$dotnet" "$tool/uasset-dump.dll" --dataset "$root" "$out" < "$b" > "$b.out" 2> "$b.err"
  echo $? > "$b.status"
  echo "$(ls "$out"/batches/*.status | wc -l)/$total $(date +%T)" > "$out/progress"
done
cat "$out"/batches/b.*.out | grep '^index' | cut -f2- > "$out/index.tsv"
cat "$out"/batches/b.*.out | grep -v '^index' > "$out/residue.tsv"
echo "batches $(ls "$out"/batches/*.status | wc -l), nonzero $(grep -L '^0$' "$out"/batches/*.status | wc -l), packages $(wc -l < "$out/index.tsv"), residue lines $(wc -l < "$out/residue.tsv")" | tee "$out/summary"
