using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>Native serializers that add data after an actor's or component's tagged properties.</summary>
[Flags]
public enum NativeClassTail
{
    None = 0,
    ActorComponent = 1,
    SceneComponent = 2,
    StaticMeshComponent = 4,
    Actor = 8,
}

/// <summary>One LOD's per-component data (StaticMeshComponent.cpp operator&lt;&lt;(FArchive&amp;, FStaticMeshComponentLODInfo&amp;)).</summary>
public class FStaticMeshComponentLODInfo
{
    [JsonProperty] public byte GlobalStripFlags;
    [JsonProperty] public byte ClassStripFlags;
    [JsonProperty] public Guid OriginalMapBuildDataId;
    /// <summary>Override vertex colours: stride, vertex count, and the colour buffer's own strip flags and element size.</summary>
    [JsonProperty] public bool HasOverrideVertexColors;
    [JsonProperty] public byte[] ColorBufferStripFlags;
    [JsonProperty] public int ColorStride;
    [JsonProperty] public int ColorNumVertices;
    [JsonProperty] public int ColorElementSize;
    [JsonProperty] public byte[] ColorData;
    /// <summary>Mesh-paint source vertices, kept as their serialized bytes: position, normal and colour per vertex.</summary>
    [JsonProperty] public int PaintedVertexCount;
    [JsonProperty] public byte[] PaintedVertices;
}

/// <summary>The data native actor and component serializers write after the tagged properties, base class first.</summary>
public class NativeClassTailData
{
    [JsonProperty] public NativeClassTail Tails;
    [JsonProperty] public List<FSimpleMemberReferenceData> UCSModifiedProperties;
    [JsonProperty] public bool? SceneComponentIsCooked;
    [JsonProperty] public byte[] SceneComponentCookedBounds;
    [JsonProperty] public List<FStaticMeshComponentLODInfo> StaticMeshLODData;
    /// <summary>StaticMeshComponent.cpp: whether the mesh paint texture is serialized natively (cooked data only).</summary>
    [JsonProperty] public bool? MeshPaintTextureIsCooked;
    [JsonProperty] public FPackageIndex MeshPaintTextureCooked;
    [JsonProperty] public bool? ActorIsCooked;
    [JsonProperty] public FString ActorLabel;
}

public class FSimpleMemberReferenceData
{
    [JsonProperty] public FPackageIndex MemberParent;
    [JsonProperty] public FName MemberName;
    [JsonProperty] public Guid MemberGuid;
}

internal static class NativeClassTails
{
    static readonly Guid FortniteReleaseGuid = UAPUtils.GUID(0xE7086368, 0x6B234C58, 0x84391B70, 0x16265E91);
    static readonly Guid UE5SpecialProjectGuid = UAPUtils.GUID(0x59DA5D52, 0x12324948, 0xB8785978, 0x70B8E98B);
    static readonly Guid RenderingGuid = UAPUtils.GUID(0x12F88B9F, 0x88754AFC, 0xA67CD90C, 0x383ABD29);
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    const int ActorComponentUCSModifiedPropertiesSparseStorage = 4, SerializeSceneComponentStaticBounds = 2,
        SerializeActorLabelInCookedBuilds = 4, IncreaseNormalPrecision = 26, MeshPaintTextureUsesEditorOnly = 166;

    // Blueprint class package path + class name -> its nearest native class, across all packages read in this process.
    static readonly ConcurrentDictionary<string, (string Module, string Class)?> BlueprintNativeBase = new();

    /// <summary>Reads the tails from an export's leftover bytes, keeping them only if they account for every byte.</summary>
    internal static void TryRead(UAsset asset, Export export)
    {
        if (export is not NormalExport normal || export.Extras == null || export.Extras.Length == 0) return;
        if (asset.IsFilterEditorOnly || asset.HasUnversionedProperties) return;
        // LinkerLoad.cpp loads a class default object through UClass::SerializeDefaultObject, which reads only its tagged
        // properties; the class's own Serialize, and so its tail, never runs for it
        if (export.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject)) return;
        var native = NativeBaseOf(asset, export.ClassIndex);
        if (native == null) return;
        if (TailsOf(asset, native.Value) is not NativeClassTail tails) return;
        try
        {
            using var stream = new MemoryStream(export.Extras);
            var reader = new AssetBinaryReader(stream, asset);
            var data = new NativeClassTailData { Tails = tails };
            if (tails.HasFlag(NativeClassTail.ActorComponent) && asset.GetCustomVersion(FortniteReleaseGuid) >= ActorComponentUCSModifiedPropertiesSparseStorage)
            {
                int n = reader.ReadInt32();
                if (n < 0 || n > 100000) return;
                data.UCSModifiedProperties = new List<FSimpleMemberReferenceData>(n);
                for (int i = 0; i < n; i++)
                    data.UCSModifiedProperties.Add(new FSimpleMemberReferenceData { MemberParent = new FPackageIndex(reader), MemberName = reader.ReadFName(), MemberGuid = reader.ReadGuid() });
            }
            if (tails.HasFlag(NativeClassTail.SceneComponent) && ComputesBoundsOnce(normal, native.Value) && asset.GetCustomVersion(UE5SpecialProjectGuid) >= SerializeSceneComponentStaticBounds)
            {
                data.SceneComponentIsCooked = reader.ReadBooleanInt();
                if (data.SceneComponentIsCooked == true) data.SceneComponentCookedBounds = reader.ReadBytes(BoundsSize(asset));
            }
            if (tails.HasFlag(NativeClassTail.StaticMeshComponent))
            {
                int n = reader.ReadInt32();
                if (n < 0 || n > 64) return;
                data.StaticMeshLODData = new List<FStaticMeshComponentLODInfo>(n);
                for (int i = 0; i < n; i++) data.StaticMeshLODData.Add(ReadLODInfo(reader, asset));
                if (asset.GetCustomVersion(FortniteMainGuid) >= MeshPaintTextureUsesEditorOnly)
                {
                    data.MeshPaintTextureIsCooked = reader.ReadBooleanInt();
                    if (data.MeshPaintTextureIsCooked == true) data.MeshPaintTextureCooked = new FPackageIndex(reader);
                }
            }
            if (tails.HasFlag(NativeClassTail.Actor) && asset.GetCustomVersion(UE5SpecialProjectGuid) >= SerializeActorLabelInCookedBuilds)
            {
                data.ActorIsCooked = reader.ReadBooleanInt();
                if (data.ActorIsCooked == true) data.ActorLabel = reader.ReadFString();
            }
            if (stream.Position != stream.Length) return;
            export.NativeTails = data;
            export.Extras = Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException) { }
    }

    internal static void Write(AssetBinaryWriter writer, NativeClassTailData data)
    {
        var asset = (UAsset)writer.Asset;
        if (data.UCSModifiedProperties != null)
        {
            writer.Write(data.UCSModifiedProperties.Count);
            foreach (var r in data.UCSModifiedProperties) { r.MemberParent.Write(writer); writer.Write(r.MemberName); writer.Write(r.MemberGuid); }
        }
        if (data.SceneComponentIsCooked != null)
        {
            writer.WriteBooleanInt(data.SceneComponentIsCooked.Value);
            if (data.SceneComponentIsCooked.Value) writer.Write(data.SceneComponentCookedBounds);
        }
        if (data.StaticMeshLODData != null)
        {
            writer.Write(data.StaticMeshLODData.Count);
            foreach (var lod in data.StaticMeshLODData) WriteLODInfo(writer, asset, lod);
            if (data.MeshPaintTextureIsCooked != null)
            {
                writer.WriteBooleanInt(data.MeshPaintTextureIsCooked.Value);
                if (data.MeshPaintTextureIsCooked.Value) data.MeshPaintTextureCooked.Write(writer);
            }
        }
        if (data.ActorIsCooked != null)
        {
            writer.WriteBooleanInt(data.ActorIsCooked.Value);
            if (data.ActorIsCooked.Value) writer.Write(data.ActorLabel);
        }
    }

    static int BoundsSize(UAsset asset) => asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES ? 7 * 8 : 7 * 4;

    // StaticMeshComponent.h FPaintedVertex: position (FVector), normal (FVector4f, or a packed normal before
    // IncreaseNormalPrecision), colour.
    static int PaintedVertexSize(UAsset asset) =>
        (asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES ? 24 : 12) +
        (asset.GetCustomVersion(RenderingGuid) >= IncreaseNormalPrecision ? 16 : 4) + 4;

    /// <summary>SceneComponent.cpp writes its cooked-bounds flag only when bComputeBoundsOnceForGame is set.</summary>
    static bool ComputesBoundsOnce(NormalExport export, (string Module, string Class) native)
    {
        var saved = export.Data?.FirstOrDefault(p => p.Name?.Value?.Value == "bComputeBoundsOnceForGame") as BoolPropertyData;
        if (saved != null) return saved.Value;
        // Native default: false, except UWaterBodyMeshComponent's constructor, which sets it.
        return native == ("/Script/Water", "WaterBodyMeshComponent");
    }

    static FStaticMeshComponentLODInfo ReadLODInfo(AssetBinaryReader reader, UAsset asset)
    {
        var lod = new FStaticMeshComponentLODInfo { GlobalStripFlags = reader.ReadByte(), ClassStripFlags = reader.ReadByte() };
        if ((lod.GlobalStripFlags & 2) == 0) lod.OriginalMapBuildDataId = reader.ReadGuid();
        if ((lod.ClassStripFlags & 1) == 0)
        {
            lod.HasOverrideVertexColors = reader.ReadByte() != 0;
            if (lod.HasOverrideVertexColors)
            {
                lod.ColorBufferStripFlags = reader.ReadBytes(2);
                lod.ColorStride = reader.ReadInt32();
                lod.ColorNumVertices = reader.ReadInt32();
                if (lod.ColorNumVertices > 0 && (lod.ColorBufferStripFlags[0] & 2) == 0)
                {
                    lod.ColorElementSize = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    if (count < 0 || lod.ColorElementSize != 4) throw new FormatException("Unexpected vertex colour buffer");
                    lod.ColorData = reader.ReadBytes(count * lod.ColorElementSize);
                }
            }
        }
        if ((lod.GlobalStripFlags & 1) == 0)
        {
            lod.PaintedVertexCount = reader.ReadInt32();
            if (lod.PaintedVertexCount < 0) throw new FormatException("Invalid painted vertex count");
            lod.PaintedVertices = reader.ReadBytes(lod.PaintedVertexCount * PaintedVertexSize(asset));
        }
        return lod;
    }

    static void WriteLODInfo(AssetBinaryWriter writer, UAsset asset, FStaticMeshComponentLODInfo lod)
    {
        writer.Write(lod.GlobalStripFlags);
        writer.Write(lod.ClassStripFlags);
        if ((lod.GlobalStripFlags & 2) == 0) writer.Write(lod.OriginalMapBuildDataId);
        if ((lod.ClassStripFlags & 1) == 0)
        {
            writer.Write((byte)(lod.HasOverrideVertexColors ? 1 : 0));
            if (lod.HasOverrideVertexColors)
            {
                writer.Write(lod.ColorBufferStripFlags);
                writer.Write(lod.ColorStride);
                writer.Write(lod.ColorNumVertices);
                if (lod.ColorNumVertices > 0 && (lod.ColorBufferStripFlags[0] & 2) == 0)
                {
                    writer.Write(lod.ColorElementSize);
                    writer.Write(lod.ColorData.Length / lod.ColorElementSize);
                    writer.Write(lod.ColorData);
                }
            }
        }
        if ((lod.GlobalStripFlags & 1) == 0)
        {
            writer.Write(lod.PaintedVertexCount);
            writer.Write(lod.PaintedVertices);
        }
    }

    /// <summary>
    /// The nearest native class of an export's class. A Blueprint class is followed through its SuperStruct, in this
    /// package or, through <see cref="UAsset.FindAssetOnDiskFromPath"/>, in the package that defines it.
    /// </summary>
    /// <summary>
    /// The engine table names engine classes. A project or plugin C++ class outside it inherits its nearest table
    /// ancestor's tails, found through the mappings' super chain; with no mappings or no table ancestor it has none.
    /// </summary>
    static NativeClassTail? TailsOf(UAsset asset, (string Module, string Class) native)
    {
        for (int depth = 0; depth < 64; depth++)
        {
            if (NativeClassTailTable.Tails.TryGetValue(native, out var tails)) return tails;
            var schema = asset.Mappings?.GetSchemaFromName(native.Class, asset, native.Module, false);
            if (schema == null || string.IsNullOrEmpty(schema.SuperType)) return null;
            string module = schema.SuperTypeModulePath;
            if (string.IsNullOrEmpty(module)) return null;
            native = (module.StartsWith("/Script/") ? module : "/Script/" + module, schema.SuperType);
        }
        return null;
    }

    static (string Module, string Class)? NativeBaseOf(UAsset asset, FPackageIndex classIndex, int depth = 0)
    {
        if (classIndex == null || classIndex.IsNull() || depth > 32) return null;
        if (classIndex.IsExport())
        {
            var exp = classIndex.ToExport(asset);
            return exp is StructExport s ? NativeBaseOf(asset, s.SuperStruct, depth + 1) : null;
        }
        var cls = classIndex.ToImport(asset);
        if (cls?.OuterIndex?.IsImport() != true) return null;
        string package = cls.OuterIndex.ToImport(asset)?.ObjectName?.ToString(), name = cls.ObjectName?.ToString();
        if (package == null || name == null) return null;
        if (package.StartsWith("/Script/")) return (package, name);
        string key = package + "." + name;
        if (BlueprintNativeBase.TryGetValue(key, out var cached)) return cached;
        (string, string)? result = null;
        try
        {
            string path = asset.FindAssetOnDiskFromPath(package);
            if (path != null && File.Exists(path))
            {
                var other = new UAsset(path, EngineVersion.UNKNOWN, null, CustomSerializationFlags.SkipParsingBytecode | CustomSerializationFlags.SkipPreloadDependencyLoading);
                var classExport = other.Exports.OfType<StructExport>().FirstOrDefault(e => e.ObjectName?.ToString() == name);
                if (classExport != null) result = NativeBaseOf(other, classExport.SuperStruct, depth + 1);
            }
        }
        catch (Exception) { }
        BlueprintNativeBase[key] = result;
        return result;
    }
}
