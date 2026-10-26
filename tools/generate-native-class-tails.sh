#!/usr/bin/env bash
# Generates UAssetAPI/ExportTypes/NativeClassTails.g.cs from an Unreal Engine source tree.
# Usage: tools/generate-native-class-tails.sh <Engine directory> [extra source roots...]
#
# Lists every native class descending from AActor, UActorComponent, UBlueprintCore, UNiagaraDataInterfaceTexture, UWorld, UWorldPartition, UMaterialInterface, UMaterial, UMaterialInterfaceEditorOnlyData, UBodySetup, UObjectRedirector, USkyAtmosphereComponent, UPCGComponent, UStaticMesh, UAnimationAsset, UControlRig, URigVMMemoryStorageGeneratorClass, URigVMBlueprintGeneratedClass, UControlRigBlueprintGeneratedClass UUAFAnimGraph, USoundNode, USoundCue or USoundNodeWavePlayer, with its module (the nearest *.Build.cs) and
# which of these serializers, each run after the tagged properties and base class first, write persistent data for it:
#   UActorComponent      Components/ActorComponent.cpp: UCSModifiedProperties
#   USceneComponent      Components/SceneComponent.cpp: cooked static bounds, when bComputeBoundsOnceForGame
#   UStaticMeshComponent Components/StaticMeshComponent.cpp: LODData
#   AActor               Actor.cpp: bIsCooked and the cooked actor label
#   UBlueprintCore       Blueprint.cpp: a legacy authority flag, read from packages before
#                        FFrameworkObjectVersion::BlueprintGeneratedClassIsAlwaysAuthoritative
#   UNiagaraDataInterfaceTexture NiagaraDataInterfaceTexture.cpp: StreamData, from
#                        FNiagaraCustomVersion::TextureDataInterfaceUsesCustomSerialize
#   UWorld               World.cpp: PersistentLevel, ExtraReferencedObjects and the persistent StreamingLevels
#   UWorldPartition      WorldPartition.cpp: the cooked flag and, when cooked, StreamingPolicy, from
#                        FUE5MainStreamObjectVersion::WorldPartitionSerializeStreamingPolicyOnCook
#   UMaterialInterface   MaterialInterface.cpp: the saved-cached-expression-data flag, from
#                        FUE5ReleaseStreamObjectVersion::MaterialInterfaceSavedCachedData
#   UMaterialInstance    MaterialInstance.cpp: the saved-cached-data flag, and the inline shader map count when
#                        bHasStaticPermutationResource
#   UMaterial           Materials/Material.cpp: the inline shader map count, the legacy saved-cached-expression-data flag
#                        between FUE5MainStreamObjectVersion::MaterialSavedCachedData and
#                        FUE5ReleaseStreamObjectVersion::MaterialInterfaceSavedCachedData, and the force-Nanite-usage flag
#                        from FRenderingObjectVersion::NaniteForceMaterialUsage
#   UMaterialInterfaceEditorOnlyData MaterialInterface.cpp: the saved-cached-expression-data flag
#   UBodySetup           PhysicsEngine/BodySetup.cpp: BodySetupGuid, the cooked flag, and the level-set implicit
#                        objects saved between two Fortnite versions
#   UObjectRedirector    ObjectRedirector.cpp: DestinationObject
#   USkyAtmosphereComponent Components/SkyAtmosphereComponent.cpp: bStaticLightingBuiltGUID
#   UPCGComponent        PCG PCGComponent.cpp and ChangeTracking/PCGChangeTracker.cpp: the dynamically tracked
#                        selection keys and their settings, from FPCGCustomVersion::DynamicTrackingKeysSerializedInComponent
#   UStaticMesh          StaticMesh.cpp: strip flags, body setup, nav collision, lighting GUID, sockets and static
#                        materials; legacy raw meshes, cooked render data and SpeedTree wind keep their bytes
#   UAnimationAsset      Animation/AnimationAsset.cpp: SkeletonGuid
#   UAnimSequence        Animation/AnimSequence.cpp: strip flags, raw tracks, legacy source tracks and compressed data,
#                        and the serialize-compressed-data flag; compressed data itself keeps its bytes
#   UControlRig          ControlRig.cpp: the physics solver descriptions
#   URigVMMemoryStorageGeneratorClass RigVMCore/RigVMMemoryStorage.cpp: property path descriptions and memory type
#   URigVMBlueprintGeneratedClass RigVMBlueprintGeneratedClass.cpp and UControlRigBlueprintGeneratedClass
#                        ControlRigBlueprintGeneratedClass.cpp: the class's VM, serialized inline, and its graph function store
#   UUAFAnimGraph        UAFAnimGraph Graph/AnimNextAnimationGraph.cpp: the shared data archive buffer, from
#                        FFortniteMainBranchObjectVersion::AnimNextCombineParameterBlocksAndGraphs
#   USoundNode           Sound/SoundNode.cpp, and USoundCue SoundCue.cpp: strip flags and, with editor data, the graph
#                        node or the cue's graph
#   Every class with a `case` in ClassSerializers.cs, which names each serializer: listed per class as a chain of
#                        those ancestors, base first. USoundWaveProcedural::Serialize calls USoundBase's, skipping USoundWave.
#   USoundNodeWavePlayer Sound/SoundNodeWavePlayer.cpp: the hard sound wave reference, from
#                        FFrameworkObjectVersion::HardSoundReferences
# Other serializers of these classes in 5.8.3 save nothing after the tagged properties outside migration or cooking,
# but some still read data that older engines saved; those reads are added as audits of older packages find them.
# Recheck the Serialize overrides of these classes when regenerating for a new engine release.
set -euo pipefail
engine=$1; shift
out=$(dirname "$0")/../UAssetAPI/ExportTypes/NativeClassTails.g.cs
tmp=$(mktemp -d); trap 'rm -rf "$tmp"' EXIT
# the classes whose serializers ClassSerializers.cs reads
named=$(sed -n 's/^ *case "\([UA][A-Za-z0-9_]*\)":.*/\1/p' "$(dirname "$0")/../UAssetAPI/ExportTypes/ClassSerializers.cs" | sort -u | tr '\n' ' ')
[ -n "$named" ] || { echo "no class serializers found" >&2; exit 1; }

# a declaration can carry macros before the name, such as an export macro or UE_DEPRECATED(version, "message")
rg --no-config --no-heading --with-filename -U -P -o --glob '*.h' \
  -e 'class\s+(?:\w+(?:\([^)]*\))?\s+)*?([UA][A-Z]\w*)\s*(?:final\s*)?:\s*public\s+(?:\w+::)*([UA]\w+)' -r '$1 $2' \
  "$engine/Source" "$engine/Plugins" "$@" |
  awk -F: '{split($NF,a," "); if (a[1] != a[2]) print a[1]"\t"a[2]"\t"$1}' | sort -u > "$tmp/inherit.tsv"

awk -F'\t' -v named_list="$named" '{p[$1]=$2; f[$1]=$3}
  END {
    n = split(named_list, nl, " "); for (i = 1; i <= n; i++) named[nl[i]] = 1
    # Serialize overrides that call an older ancestor Serialize, skipping the classes in between
    skips["USoundWaveProcedural"] = "USoundBase"
    for (c in p) {
      x=c; flags=""; chain=""; skip=""; depth=0
      # a class named like its namespaced parent can make a cycle
      while (x != "" && depth++ < 64) {
        if (x=="UStaticMeshComponent") flags="StaticMeshComponent|" flags
        if (x=="USceneComponent") flags="SceneComponent|" flags
        if (x=="UActorComponent") flags="ActorComponent|" flags
        if (x=="AActor") flags="Actor|" flags
        if (x=="UBlueprintCore") flags="BlueprintCore|" flags
        if (x=="UNiagaraDataInterfaceTexture") flags="NiagaraTexture|" flags
        if (x=="UWorld") flags="World|" flags
        if (x=="UWorldPartition") flags="WorldPartition|" flags
        if (x=="UMaterialInstance") flags="MaterialInstance|" flags
        if (x=="UMaterialInterface") flags="MaterialInterface|" flags
        if (x=="UMaterial") flags="Material|" flags
        if (x=="UMaterialInterfaceEditorOnlyData") flags="MaterialEditorOnlyData|" flags
        if (x=="UBodySetup") flags="BodySetup|" flags
        if (x=="UObjectRedirector") flags="ObjectRedirector|" flags
        if (x=="USkyAtmosphereComponent") flags="SkyAtmosphere|" flags
        if (x=="UPCGComponent") flags="PCGComponent|" flags
        if (x=="UStaticMesh") flags="StaticMesh|" flags
        if (x=="UAnimationAsset") flags="AnimationAsset|" flags
        if (x=="UAnimSequence") flags="AnimSequence|" flags
        if (x=="UControlRig") flags="ControlRig|" flags
        if (x=="URigVMMemoryStorageGeneratorClass") flags="RigVMMemoryStorage|" flags
        if (x=="URigVMBlueprintGeneratedClass") flags="RigVMClass|" flags
        if (x=="UControlRigBlueprintGeneratedClass") flags="ControlRigClass|" flags
        if (x=="UUAFAnimGraph") flags="UAFAnimGraph|" flags
        if (x=="USoundNode" || x=="USoundCue") flags="SoundNode|" flags
        if (x=="USoundNodeWavePlayer") flags="SoundNodeWavePlayer|" flags
        if (skip != "") { if (x == skip) skip = ""; else { x = (x in p) ? p[x] : ""; continue } }
        if (x in named) chain = x (chain == "" ? "" : ",") chain
        if (x in skips) skip = skips[x]
        x = (x in p) ? p[x] : ""
      }
      if (flags == "" && chain == "") continue
      sub(/\|$/, "", flags)
      print substr(c,2)"\t"(flags == "" ? "None" : flags)"\t"(chain == "" ? "-" : chain)"\t"f[c]
    }
    print "Actor\tActor\t-\t" f["AActor"]; print "BlueprintCore\tBlueprintCore\t-\t" f["UBlueprintCore"]; print "NiagaraDataInterfaceTexture\tNiagaraTexture\t-\t" f["UNiagaraDataInterfaceTexture"];print "ActorComponent\tActorComponent\t-\t" f["UActorComponent"]
  }' "$tmp/inherit.tsv" | sort -u |
while IFS=$'\t' read -r cls flags chain header; do
  dir=$(dirname "$header"); module=""
  while [ "$dir" != "/" ]; do
    build=$(ls "$dir"/*.[Bb]uild.cs 2>/dev/null | head -1 || true)
    if [ -n "$build" ]; then module=$(basename "$build"); module=${module%.*.cs}; break; fi
    dir=$(dirname "$dir")
  done
  [ -n "$module" ] || { echo "no module for $cls ($header)" >&2; exit 1; }
  printf '%s\t%s\t%s\t%s\n' "$module" "$cls" "$flags" "$chain"
done | sort > "$tmp/table.tsv"

# [CoreRedirects] ClassRedirects from the engine and plugin configuration: a package saved before a class moved or was
# renamed imports it by its old name (the documentation examples in BaseEngine.ini name /Script/Module and GameName)
rg --no-config -o -N --no-filename -g '*.ini' 'ClassRedirects\s*=\s*\(([^)]*)\)' -r '$1' "$engine/Config" "$engine/Plugins" "$@" |
  grep -v 'MatchSubstring' |
  sed -nE 's/.*OldName\s*=\s*"([^"]+)".*NewName\s*=\s*"\/Script\/([^".]+)\.([^"]+)".*/\1\t\2\t\3/p' |
  grep -vE '^/Script/Module\.|^OldClass\b|/Script/GameName' | sort -u -t$'\t' -k1,1 > "$tmp/redirects.tsv"

# Native classes deriving from UClass, whose exports are classes and serialize as UClass does
awk -F'\t' '{p[$1]=$2} END { for (c in p) { x=p[c]; depth=0; while (x != "" && depth++ < 64) { if (x=="UClass") { print substr(c,2); break } x = (x in p) ? p[x] : "" } } }' "$tmp/inherit.tsv" | sort -u > "$tmp/classkinds.txt"

{
  echo '// Generated by tools/generate-native-class-tails.sh. Do not edit.'
  echo 'using System.Collections.Generic;'
  echo
  echo 'namespace UAssetAPI.ExportTypes;'
  echo
  echo 'internal static class NativeClassTailTable'
  echo '{'
  echo '    /// <summary>Native actor and component classes, keyed by /Script module and class name, with the serializers that add data after their tagged properties.</summary>'
  echo '    internal static readonly Dictionary<(string Module, string Class), NativeClassTail> Tails = new()'
  echo '    {'
  awk -F'\t' '{gsub(/\|/, " | NativeClassTail.", $3); printf "        [(\"/Script/%s\", \"%s\")] = NativeClassTail.%s,\n", $1, $2, $3}' "$tmp/table.tsv"
  echo '    };'
  echo
  echo '    /// <summary>The ancestors, base first, whose serializers <see cref="ClassSerializers"/> reads, by native class.</summary>'
  echo '    internal static readonly Dictionary<(string Module, string Class), string[]> Chains = new()'
  echo '    {'
  awk -F'\t' '$4 != "-" {n = split($4, a, ","); printf "        [(\"/Script/%s\", \"%s\")] = [", $1, $2; for (i = 1; i <= n; i++) printf "%s\"%s\"", (i > 1 ? ", " : ""), a[i]; print "],"}' "$tmp/table.tsv"
  echo '    };'
  echo
  echo '    /// <summary>[CoreRedirects] ClassRedirects: a class by its old /Script path or bare name, and its current module and name.</summary>'
  echo '    internal static readonly Dictionary<string, (string Module, string Class)> ClassRedirects = new()'
  echo '    {'
  awk -F'\t' '{printf "        [\"%s\"] = (\"/Script/%s\", \"%s\"),\n", $1, $2, $3}' "$tmp/redirects.tsv"
  echo '    };'
  echo
  echo '    /// <summary>Native classes deriving from UClass, by name: an export of one of them is itself a class.</summary>'
  echo '    internal static readonly HashSet<string> ClassDerived = new()'
  echo '    {'
  awk '{printf "        \"%s\",\n", $1}' "$tmp/classkinds.txt"
  echo '    };'
  echo '}'
} > "$out"
echo "wrote $(wc -l < "$tmp/table.tsv") classes and $(wc -l < "$tmp/classkinds.txt") class kinds to $out"
