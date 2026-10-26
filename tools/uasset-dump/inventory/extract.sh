#!/usr/bin/env bash
# extract.sh <signature regex> <out>: every function body whose signature line matches, from the engine source
cd ~/.local/opt/unreal-engine/5.8.3/Engine
rg --no-config -P -U --multiline-dotall --json -g '*.cpp' -g '*.h' -g '*.inl' "(?m)^[^\n;#/]*(?:$1)[^\n{;]*(?:\n[^\n{;]*){0,2}?\s*(\{(?:[^{}]++|(?1))*\})" Source Plugins 2>/dev/null |
python3 -c '
import json,sys
for l in sys.stdin:
    j=json.loads(l)
    if j["type"]!="match": continue
    print("#####",j["data"]["path"]["text"].split("Engine/")[-1]); print(j["data"]["lines"]["text"])' > "$2"
grep -c '^#####' "$2"
