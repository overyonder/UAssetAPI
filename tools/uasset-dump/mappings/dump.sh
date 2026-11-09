#!/usr/bin/env bash
# dump.sh <project.uproject> <name>: starts the project's 5.8.3 editor headless, dumps its reflection data with jmap to
# <name>-5.8.3.jmap (schemas and native defaults) and <name>-5.8.3.usmap (schemas for UAssetAPI), then closes the editor.
set -euo pipefail
here=$(cd "$(dirname "$0")" && pwd)
project=$1 name=$2
engine=~/.local/opt/unreal-engine/5.8.3/Engine/Binaries/Linux
dumper=~/code/github.com/trumank/jmap/target/release/jmap_dumper
fifos=$(mktemp -d)
editor=
# a failure must not leave the editor blocked in hold.py
reader=
trap '[ -n "$editor" ] && kill $editor 2>/dev/null; [ -n "$reader" ] && kill $reader 2>/dev/null; rm -rf "$fifos"' EXIT
mkfifo "$fifos/pid" "$fifos/release"

JMAP_FIFOS="$fifos" systemd-run --user --quiet --scope --collect --unit="jmap-$name" -p MemoryMax=12G \
    "$engine/UnrealEditor-Cmd" "$project" -run=pythonscript -script="$here/hold.py" -EnablePlugins=PythonScriptPlugin \
    -unattended -nullrhi -nosplash -nop4 &
editor=$!
# the pid arrives once the editor is up; if the editor exits first, it never will
cat "$fifos/pid" > "$fifos/pid.txt" &
reader=$!
wait -n $editor $reader || true
if ! kill -0 $editor 2>/dev/null; then kill $reader 2>/dev/null; echo "editor exited before it was ready; see the project log" >&2; exit 1; fi
reader=
pid=$(cat "$fifos/pid.txt")

# The editor executable is not position independent and holds GUObjectArray as a copy relocation at a fixed address.
# NamePoolData is a static in libUnrealEditor-Core.so; its offset comes from the shipped debug symbols.
objects=0x$(nm -D "$engine/UnrealEditor-Cmd" | awk '$3=="GUObjectArray"{print $1}')
pool_offset=0x$(nm -C "$engine/libUnrealEditor-Core.debug" | awk '$3=="NamePoolData"{print $1}')
[ "$pool_offset" != 0x ] || { echo "NamePoolData not found in the Core debug symbols" >&2; exit 1; }
core_base=0x$(awk '/libUnrealEditor-Core\.so$/ && $3=="00000000" {split($1, r, "-"); print r[1]; exit}' "/proc/$pid/maps")
pool=$(printf '%x' $((core_base + pool_offset)))

nice "$dumper" --pid "$pid" --fname-pool "$pool" --guobject-array "${objects#0x}" --image-base 200000 \
    --engine-version 5.8 --editor --stats --case-preserving --target x86_64-unknown-linux-gnu "$here/$name-5.8.3.jmap"
echo go > "$fifos/release"
nice "$dumper" --jmap "$here/$name-5.8.3.jmap" "$here/$name-5.8.3.usmap"
# the editor may hang on shutdown (ML Deformer's Python dependency installer does), so it gets a minute
timeout 60 tail --pid=$editor -f /dev/null || kill -9 $editor 2>/dev/null || true
wait $editor || true
editor=
ls -la "$here/$name-5.8.3.jmap" "$here/$name-5.8.3.usmap"
