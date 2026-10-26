using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.CustomVersions;
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
    BlueprintCore = 16,
    NiagaraTexture = 32,
    World = 64,
    WorldPartition = 128,
    MaterialInstance = 256,
    MaterialEditorOnlyData = 512,
    BodySetup = 1024,
    MaterialInterface = 2048,
    ObjectRedirector = 4096,
    SkyAtmosphere = 8192,
    PCGComponent = 16384,
    StaticMesh = 32768,
    AnimationAsset = 65536,
    AnimSequence = 131072,
    ControlRig = 262144,
    RigVMMemoryStorage = 524288,
    RigVMClass = 1048576,
    ControlRigClass = 2097152,
}

/// <summary>RigVMCore/RigVMPropertyPath.h operator&lt;&lt;(FArchive&amp;, FRigVMPropertyPathDescription&amp;).</summary>
public class FRigVMPropertyPathDescriptionData
{
    [JsonProperty] public int PropertyIndex;
    [JsonProperty] public FString HeadCPPType;
    [JsonProperty] public FString SegmentPath;
}

/// <summary>StaticMesh.cpp operator&lt;&lt;(FArchive&amp;, FStaticMaterial&amp;), with FMeshUVChannelInfo inline.</summary>
public class FStaticMaterialData
{
    [JsonProperty] public FPackageIndex MaterialInterface;
    [JsonProperty] public FName MaterialSlotName;
    [JsonProperty] public FName ImportedMaterialSlotName;
    [JsonProperty] public bool? UVChannelInitialized;
    [JsonProperty] public bool? UVChannelOverrideDensities;
    [JsonProperty] public float[] UVChannelLocalUVDensities;
    [JsonProperty] public FPackageIndex OverlayMaterialInterface;
}

/// <summary>PCGSettings.h FPCGSettingsAndCulling: a settings object and whether its key is culled.</summary>
public class FPCGTrackedSettings
{
    [JsonProperty] public FSoftObjectPath Settings;
    [JsonProperty] public bool bCulled;
}

/// <summary>One entry of FPCGChangeTracker's DynamicallyTrackedKeysToSettings: an FPCGSelectionKey, serialized as its
/// tagged properties, and the settings that track it.</summary>
public class FPCGTrackedKey
{
    [JsonProperty] public List<PropertyData> Key;
    [JsonProperty] public List<FPCGTrackedSettings> Settings;
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
    /// <summary>Blueprint.cpp UBlueprintCore::Serialize: bLegacyGeneratedClassIsAuthoritative, read and discarded from
    /// packages before FFrameworkObjectVersion::BlueprintGeneratedClassIsAlwaysAuthoritative.</summary>
    [JsonProperty] public bool? LegacyGeneratedClassIsAuthoritative;
    /// <summary>NiagaraDataInterfaceTexture.cpp: StreamData, the texture's stream data array.</summary>
    [JsonProperty] public byte[] NiagaraTextureStreamData;
    /// <summary>World.cpp UWorld::Serialize: the persistent level, extra referenced objects and persistent streaming levels.</summary>
    [JsonProperty] public FPackageIndex WorldPersistentLevel;
    [JsonProperty] public List<FPackageIndex> WorldExtraReferencedObjects;
    [JsonProperty] public List<FPackageIndex> WorldStreamingLevels;
    /// <summary>WorldPartition.cpp UWorldPartition::Serialize: whether it was cooked, and then its streaming policy.</summary>
    [JsonProperty] public bool? WorldPartitionIsCooked;
    [JsonProperty] public FPackageIndex WorldPartitionStreamingPolicy;
    /// <summary>MaterialInterface.cpp UMaterialInterface::Serialize: whether cooked cached expression data follows.</summary>
    [JsonProperty] public bool? MaterialInterfaceSavedCachedExpressionData;
    /// <summary>MaterialInstance.cpp: whether cooked cached data follows (only cooking sets it).</summary>
    [JsonProperty] public bool? MaterialInstanceSavedCachedData;
    /// <summary>Material.cpp SerializeInlineShaderMaps: the count of inline shader maps, zero outside cooking.</summary>
    [JsonProperty] public int? MaterialInstanceInlineShaderMaps;
    /// <summary>MaterialInterface.cpp UMaterialInterfaceEditorOnlyData: whether cooked cached expression data follows.</summary>
    [JsonProperty] public bool? MaterialSavedCachedExpressionData;
    /// <summary>PhysicsEngine/BodySetup.cpp: the body setup's GUID, whether cooked collision data follows, and the
    /// level-set implicit object count saved between LevelsetSerializationSupportForBodySetup and DisableLevelset_v14_10.</summary>
    [JsonProperty] public Guid? BodySetupGuid;
    [JsonProperty] public bool? BodySetupIsCooked;
    [JsonProperty] public int? BodySetupImplicitObjects;
    /// <summary>ObjectRedirector.cpp: the object the redirector points to.</summary>
    [JsonProperty] public FPackageIndex RedirectorDestination;
    /// <summary>Components/SkyAtmosphereComponent.cpp: bStaticLightingBuiltGUID.</summary>
    [JsonProperty] public Guid? SkyAtmosphereLightingGuid;
    /// <summary>PCG ChangeTracking/PCGChangeTracker.cpp: DynamicallyTrackedKeysToSettings.</summary>
    [JsonProperty] public List<FPCGTrackedKey> PCGTrackedKeys;
    /// <summary>StaticMesh.cpp UStaticMesh::Serialize, in order.</summary>
    [JsonProperty] public byte[] StaticMeshStripFlags;
    [JsonProperty] public bool? StaticMeshIsCooked;
    [JsonProperty] public FPackageIndex StaticMeshBodySetup;
    [JsonProperty] public FPackageIndex StaticMeshNavCollision;
    /// <summary>Thumbnail angle and distance, saved before VER_UE4_DEPRECATED_STATIC_MESH_THUMBNAIL_PROPERTIES_REMOVED.</summary>
    [JsonProperty] public byte[] StaticMeshLegacyThumbnail;
    [JsonProperty] public FString StaticMeshLegacyHighResSourceMeshName;
    [JsonProperty] public uint? StaticMeshLegacyHighResSourceMeshCRC;
    [JsonProperty] public Guid? StaticMeshLightingGuid;
    [JsonProperty] public List<FPackageIndex> StaticMeshSockets;
    /// <summary>Per source model, the mesh-description-valid flag saved between StaticMeshDeprecatedRawMesh and SerializeMeshDescriptionBase.</summary>
    [JsonProperty] public List<bool> StaticMeshLegacyDescriptionValid;
    [JsonProperty] public bool? StaticMeshHasSpeedTreeWind;
    [JsonProperty] public List<FStaticMaterialData> StaticMeshMaterials;
    /// <summary>Animation/AnimationAsset.cpp: the skeleton's GUID when the asset was saved.</summary>
    [JsonProperty] public Guid? SkeletonGuid;
    /// <summary>Animation/AnimSequence.cpp UAnimSequence::Serialize: strip flags, then the raw tracks as stored
    /// (FRawAnimSequenceTrack position, rotation and scale key arrays, each as element size, count and bytes).</summary>
    [JsonProperty] public byte[] AnimSequenceStripFlags;
    [JsonProperty] public byte[] AnimSequenceRawTracks;
    /// <summary>Source raw tracks, saved from VER_UE4_ANIMATION_ADD_TRACKCURVES until RemovingSourceAnimationData.</summary>
    [JsonProperty] public byte[] AnimSequenceSourceRawTracks;
    /// <summary>Compressed data saved inline before FFrameworkObjectVersion::MoveCompressedAnimDataToTheDDC.</summary>
    [JsonProperty] public byte[] AnimSequenceLegacyCompressedData;
    [JsonProperty] public bool? AnimSequenceSerializeCompressedData;
    /// <summary>ControlRig.cpp: physics solver IDs and names, from ControlRigStoresPhysicsSolvers.</summary>
    [JsonProperty] public List<KeyValuePair<Guid, FName>> ControlRigPhysicsSolvers;
    /// <summary>RigVMCore/RigVMMemoryStorage.cpp: the storage's property paths and its ERigVMMemoryType.</summary>
    [JsonProperty] public List<FRigVMPropertyPathDescriptionData> RigVMPropertyPaths;
    [JsonProperty] public byte? RigVMMemoryType;
    /// <summary>The VM a RigVM or Control Rig blueprint generated class serializes inline, decoded, and its bytes as stored.</summary>
    [JsonProperty] public RigVMData EmbeddedVM;
    [JsonProperty] public byte[] EmbeddedVMBytes;
    /// <summary>The class's graph function store: its public functions, and their bytes for writing.</summary>
    [JsonProperty] public List<Dictionary<string, object>> GraphFunctions;
    [JsonProperty] public byte[] GraphFunctionStoreBytes;
}

public class FSimpleMemberReferenceData
{
    [JsonProperty] public FPackageIndex MemberParent;
    [JsonProperty] public FName MemberName;
    [JsonProperty] public Guid MemberGuid;
}

public static class NativeClassTails
{
    static readonly Guid FortniteReleaseGuid = UAPUtils.GUID(0xE7086368, 0x6B234C58, 0x84391B70, 0x16265E91);
    static readonly Guid UE5SpecialProjectGuid = UAPUtils.GUID(0x59DA5D52, 0x12324948, 0xB8785978, 0x70B8E98B);
    static readonly Guid RenderingGuid = UAPUtils.GUID(0x12F88B9F, 0x88754AFC, 0xA67CD90C, 0x383ABD29);
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    const int ActorComponentUCSModifiedPropertiesSparseStorage = 4, SerializeSceneComponentStaticBounds = 2,
        SerializeActorLabelInCookedBuilds = 4, IncreaseNormalPrecision = 26, MeshPaintTextureUsesEditorOnly = 166,
        WorldPartitionSerializeStreamingPolicyOnCook = 74, MaterialSavedCachedData = 36, MaterialAttributeLayerParameters = 21,
        LevelsetSerializationSupportForBodySetup = 42, DisableLevelset_v14_10 = 1, RemovedAtmosphericFog = 46,
        DynamicTrackingKeysSerializedInComponent = 20, DeprecatedHighResSourceMesh = 49, SerializeMeshDescriptionBase = 25,
        TextureStreamingMeshUVChannelData = 10, MeshMaterialSlotOverlayMaterialAdded = 196, RemovingSourceAnimationData = 3,
        ControlRigStoresPhysicsSolvers = 31, SwitchedToRigVM = 3, RigVMGeneratedClass = 104, StoreFunctionsInGeneratedClass = 24;
    static readonly Guid ControlRigGuid = UAPUtils.GUID(0xA7820CFB, 0x20A74359, 0x8C542C14, 0x9623CF50);
    static readonly Guid PCGGuid = UAPUtils.GUID(0x2763920D, 0x0F784B39, 0x986E4BB3, 0xA88D666D);

    static bool BoolProperty(NormalExport export, string name) =>
        export.Data?.Find(p => p.Name?.ToString() == name) is PropertyTypes.Objects.BoolPropertyData b && b.Value;
    static readonly Guid UE5MainStreamGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);

    /// <summary>StaticMesh.cpp UStaticMesh::Serialize; false when a branch it doesn't decode leaves the rest unread.</summary>
    static bool ReadStaticMesh(AssetBinaryReader reader, Stream stream, UAsset asset, NormalExport export, NativeClassTailData data)
    {
        data.StaticMeshStripFlags = reader.ReadBytes(2);
        bool editorDataStripped = (data.StaticMeshStripFlags[0] & 1) != 0;
        data.StaticMeshIsCooked = reader.ReadBooleanInt();
        data.StaticMeshBodySetup = new FPackageIndex(reader);
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_STATIC_MESH_STORE_NAV_COLLISION) data.StaticMeshNavCollision = new FPackageIndex(reader);
        if (!editorDataStripped)
        {
            // FRotator (three floats in these old packages) and a float
            if (asset.ObjectVersion < ObjectVersion.VER_UE4_DEPRECATED_STATIC_MESH_THUMBNAIL_PROPERTIES_REMOVED) data.StaticMeshLegacyThumbnail = reader.ReadBytes(16);
            if (asset.GetCustomVersion(RenderingGuid) < DeprecatedHighResSourceMesh)
            {
                data.StaticMeshLegacyHighResSourceMeshName = reader.ReadFString();
                data.StaticMeshLegacyHighResSourceMeshCRC = reader.ReadUInt32();
            }
        }
        data.StaticMeshLightingGuid = reader.ReadGuid();
        data.StaticMeshSockets = ReadIndices(reader, stream);
        if (!editorDataStripped)
        {
            if (asset.GetCustomVersion<FEditorObjectVersion>() < FEditorObjectVersion.StaticMeshDeprecatedRawMesh) return false;
            if (asset.GetCustomVersion(UE5MainStreamGuid) < SerializeMeshDescriptionBase)
            {
                int models = export.Data?.Find(p => p.Name?.ToString() == "SourceModels") is PropertyTypes.Objects.ArrayPropertyData a ? a.Value.Length : 0;
                data.StaticMeshLegacyDescriptionValid = new List<bool>(models);
                for (int i = 0; i < models; i++)
                {
                    bool valid = reader.ReadBooleanInt();
                    data.StaticMeshLegacyDescriptionValid.Add(valid);
                    if (valid) return false;
                }
            }
            if (asset.GetCustomVersion<FEditorObjectVersion>() < FEditorObjectVersion.UPropertryForMeshSection) return false;
        }
        if (data.StaticMeshIsCooked == true) return false;
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SPEEDTREE_STATICMESH)
        {
            data.StaticMeshHasSpeedTreeWind = reader.ReadBooleanInt();
            if (data.StaticMeshHasSpeedTreeWind == true) return false;
        }
        if (asset.GetCustomVersion<FEditorObjectVersion>() < FEditorObjectVersion.RefactorMeshEditorMaterials) return false;
        int n = reader.ReadInt32();
        if (n < 0 || n > (stream.Length - stream.Position) / 12) return false;
        data.StaticMeshMaterials = new List<FStaticMaterialData>(n);
        for (int i = 0; i < n; i++)
        {
            var m = new FStaticMaterialData { MaterialInterface = new FPackageIndex(reader), MaterialSlotName = reader.ReadFName() };
            if (!asset.IsFilterEditorOnly) m.ImportedMaterialSlotName = reader.ReadFName();
            if (asset.GetCustomVersion(RenderingGuid) >= TextureStreamingMeshUVChannelData)
            {
                m.UVChannelInitialized = reader.ReadBooleanInt();
                m.UVChannelOverrideDensities = reader.ReadBooleanInt();
                m.UVChannelLocalUVDensities = new float[4];
                for (int c = 0; c < 4; c++) m.UVChannelLocalUVDensities[c] = reader.ReadSingle();
            }
            if (asset.GetCustomVersion(FortniteMainGuid) >= MeshMaterialSlotOverlayMaterialAdded) m.OverlayMaterialInterface = new FPackageIndex(reader);
            data.StaticMeshMaterials.Add(m);
        }
        return true;
    }

    /// <summary>Animation/AnimSequence.cpp UAnimSequence::Serialize; false when compressed data follows, which it leaves unread.</summary>
    static bool ReadAnimSequence(AssetBinaryReader reader, Stream stream, UAsset asset, NativeClassTailData data)
    {
        data.AnimSequenceStripFlags = reader.ReadBytes(2);
        if ((data.AnimSequenceStripFlags[0] & 1) == 0)
        {
            data.AnimSequenceRawTracks = ReadRawTracks(reader, stream, asset);
            if (asset.ObjectVersion >= ObjectVersion.VER_UE4_ANIMATION_ADD_TRACKCURVES && asset.GetCustomVersion(UE5MainStreamGuid) < RemovingSourceAnimationData)
                data.AnimSequenceSourceRawTracks = ReadRawTracks(reader, stream, asset);
        }
        if (asset.GetCustomVersion<FFrameworkObjectVersion>() < FFrameworkObjectVersion.MoveCompressedAnimDataToTheDDC)
        {
            int n = reader.ReadInt32();
            if (n < 0 || n > stream.Length - stream.Position) return false;
            data.AnimSequenceLegacyCompressedData = reader.ReadBytes(n);
            return true;
        }
        data.AnimSequenceSerializeCompressedData = reader.ReadBooleanInt();
        return data.AnimSequenceSerializeCompressedData == false;
    }

    /// <summary>A TArray of FRawAnimSequenceTrack kept as stored: each track's key arrays are bulk serialized (element
    /// size, count, bytes), with scale keys from VER_UE4_ANIM_SUPPORT_NONUNIFORM_SCALE_ANIMATION.</summary>
    static byte[] ReadRawTracks(AssetBinaryReader reader, Stream stream, UAsset asset)
    {
        long start = stream.Position;
        int tracks = reader.ReadInt32();
        if (tracks < 0 || tracks > stream.Length - stream.Position) throw new FormatException("Invalid raw track count");
        int arrays = asset.ObjectVersion >= ObjectVersion.VER_UE4_ANIM_SUPPORT_NONUNIFORM_SCALE_ANIMATION ? 3 : 2;
        for (int t = 0; t < tracks; t++)
            for (int a = 0; a < arrays; a++)
            {
                long size = reader.ReadInt32(), count = reader.ReadInt32();
                if (size < 0 || count < 0 || size * count > stream.Length - stream.Position) throw new FormatException("Invalid raw key array");
                stream.Position += size * count;
            }
        long end = stream.Position;
        stream.Position = start;
        return reader.ReadBytes((int)(end - start));
    }

    static void WriteStaticMesh(AssetBinaryWriter writer, NativeClassTailData data)
    {
        writer.Write(data.StaticMeshStripFlags);
        writer.WriteBooleanInt(data.StaticMeshIsCooked.Value);
        data.StaticMeshBodySetup.Write(writer);
        data.StaticMeshNavCollision?.Write(writer);
        if (data.StaticMeshLegacyThumbnail != null) writer.Write(data.StaticMeshLegacyThumbnail);
        if (data.StaticMeshLegacyHighResSourceMeshCRC != null) { writer.Write(data.StaticMeshLegacyHighResSourceMeshName); writer.Write(data.StaticMeshLegacyHighResSourceMeshCRC.Value); }
        writer.Write(data.StaticMeshLightingGuid.Value);
        writer.Write(data.StaticMeshSockets.Count);
        foreach (var s in data.StaticMeshSockets) s.Write(writer);
        if (data.StaticMeshLegacyDescriptionValid != null) foreach (var v in data.StaticMeshLegacyDescriptionValid) writer.WriteBooleanInt(v);
        if (data.StaticMeshHasSpeedTreeWind != null) writer.WriteBooleanInt(data.StaticMeshHasSpeedTreeWind.Value);
        writer.Write(data.StaticMeshMaterials.Count);
        foreach (var m in data.StaticMeshMaterials)
        {
            m.MaterialInterface.Write(writer);
            writer.Write(m.MaterialSlotName);
            if (m.ImportedMaterialSlotName != null) writer.Write(m.ImportedMaterialSlotName);
            if (m.UVChannelInitialized != null)
            {
                writer.WriteBooleanInt(m.UVChannelInitialized.Value);
                writer.WriteBooleanInt(m.UVChannelOverrideDensities.Value);
                foreach (var d in m.UVChannelLocalUVDensities) writer.Write(d);
            }
            m.OverlayMaterialInterface?.Write(writer);
        }
    }

    static List<FPackageIndex> ReadIndices(AssetBinaryReader reader, Stream stream)
    {
        int n = reader.ReadInt32();
        if (n < 0 || n > (stream.Length - stream.Position) / 4) throw new FormatException("Invalid object array count");
        var list = new List<FPackageIndex>(n);
        for (int i = 0; i < n; i++) list.Add(new FPackageIndex(reader));
        return list;
    }

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
            // a legacy atmospheric fog component saved before RemovedAtmosphericFog has no lighting GUID
            if (tails.HasFlag(NativeClassTail.SkyAtmosphere) && !(native.Value.Class == "AtmosphericFogComponent" && asset.GetCustomVersion(UE5MainStreamGuid) < RemovedAtmosphericFog))
                data.SkyAtmosphereLightingGuid = reader.ReadGuid();
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
            if (tails.HasFlag(NativeClassTail.BlueprintCore) && asset.GetCustomVersion<FFrameworkObjectVersion>() < FFrameworkObjectVersion.BlueprintGeneratedClassIsAlwaysAuthoritative)
                data.LegacyGeneratedClassIsAuthoritative = reader.ReadBooleanInt();
            if (tails.HasFlag(NativeClassTail.NiagaraTexture) && asset.GetCustomVersion<FNiagaraCustomVersion>() >= FNiagaraCustomVersion.TextureDataInterfaceUsesCustomSerialize)
            {
                int n = reader.ReadInt32();
                if (n < 0 || n > stream.Length - stream.Position) return;
                data.NiagaraTextureStreamData = reader.ReadBytes(n);
            }
            // the legacy viewport and save-game summary fields come before VER_UE4_REMOVE_SAVEGAMESUMMARY; such a world keeps its bytes
            if (tails.HasFlag(NativeClassTail.World) && asset.ObjectVersion >= ObjectVersion.VER_UE4_REMOVE_SAVEGAMESUMMARY)
            {
                data.WorldPersistentLevel = new FPackageIndex(reader);
                data.WorldExtraReferencedObjects = ReadIndices(reader, stream);
                data.WorldStreamingLevels = ReadIndices(reader, stream);
            }
            if (tails.HasFlag(NativeClassTail.WorldPartition) && asset.GetCustomVersion(UE5MainStreamGuid) >= WorldPartitionSerializeStreamingPolicyOnCook)
            {
                data.WorldPartitionIsCooked = reader.ReadBooleanInt();
                if (data.WorldPartitionIsCooked == true) data.WorldPartitionStreamingPolicy = new FPackageIndex(reader);
            }
            // cooked cached data, legacy static parameters and inline shader maps aren't decoded: such a tail keeps its bytes
            if (tails.HasFlag(NativeClassTail.MaterialInterface) && asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() >= FUE5ReleaseStreamObjectVersion.MaterialInterfaceSavedCachedData)
            {
                data.MaterialInterfaceSavedCachedExpressionData = reader.ReadBooleanInt();
                if (data.MaterialInterfaceSavedCachedExpressionData == true) return;
            }
            if (tails.HasFlag(NativeClassTail.MaterialInstance))
            {
                if (asset.GetCustomVersion(UE5MainStreamGuid) >= MaterialSavedCachedData)
                {
                    data.MaterialInstanceSavedCachedData = reader.ReadBooleanInt();
                    if (data.MaterialInstanceSavedCachedData == true) return;
                }
                if (BoolProperty(normal, "bHasStaticPermutationResource") && asset.ObjectVersion >= ObjectVersion.VER_UE4_PURGED_FMATERIAL_COMPILE_OUTPUTS)
                {
                    if (asset.GetCustomVersion(RenderingGuid) < MaterialAttributeLayerParameters) return;
                    data.MaterialInstanceInlineShaderMaps = reader.ReadInt32();
                    if (data.MaterialInstanceInlineShaderMaps != 0) return;
                }
                if (asset.ObjectVersion >= ObjectVersion.VER_UE4_MATERIAL_INSTANCE_BASE_PROPERTY_OVERRIDES && asset.ObjectVersion < ObjectVersion.VER_UE4_FIX_MATERIAL_PROPERTY_OVERRIDE_SERIALIZE) return;
            }
            if (tails.HasFlag(NativeClassTail.MaterialEditorOnlyData))
            {
                data.MaterialSavedCachedExpressionData = reader.ReadBooleanInt();
                if (data.MaterialSavedCachedExpressionData == true) return;
            }
            if (tails.HasFlag(NativeClassTail.ObjectRedirector)) data.RedirectorDestination = new FPackageIndex(reader);
            if (tails.HasFlag(NativeClassTail.StaticMesh) && !ReadStaticMesh(reader, stream, asset, normal, data)) return;
            if (tails.HasFlag(NativeClassTail.AnimationAsset) && asset.ObjectVersion >= ObjectVersion.VER_UE4_SKELETON_GUID_SERIALIZATION) data.SkeletonGuid = reader.ReadGuid();
            if (tails.HasFlag(NativeClassTail.AnimSequence) && !ReadAnimSequence(reader, stream, asset, data)) return;
            if (tails.HasFlag(NativeClassTail.ControlRig) && asset.GetCustomVersion(ControlRigGuid) >= ControlRigStoresPhysicsSolvers)
            {
                int n = reader.ReadInt32();
                if (n < 0 || n > (stream.Length - stream.Position) / 24) return;
                data.ControlRigPhysicsSolvers = new List<KeyValuePair<Guid, FName>>(n);
                for (int i = 0; i < n; i++) data.ControlRigPhysicsSolvers.Add(new(reader.ReadGuid(), reader.ReadFName()));
            }
            if (tails.HasFlag(NativeClassTail.RigVMMemoryStorage))
            {
                int n = reader.ReadInt32();
                if (n < 0 || n > (stream.Length - stream.Position) / 12) return;
                data.RigVMPropertyPaths = new List<FRigVMPropertyPathDescriptionData>(n);
                for (int i = 0; i < n; i++) data.RigVMPropertyPaths.Add(new FRigVMPropertyPathDescriptionData { PropertyIndex = reader.ReadInt32(), HeadCPPType = reader.ReadFString(), SegmentPath = reader.ReadFString() });
                data.RigVMMemoryType = reader.ReadByte();
            }
            // UControlRigBlueprintGeneratedClass skips URigVMBlueprintGeneratedClass::Serialize, so a class has one VM at most
            bool controlRigVM = tails.HasFlag(NativeClassTail.ControlRigClass) && asset.GetCustomVersion(ControlRigGuid) >= SwitchedToRigVM;
            bool rigVMClassVM = !tails.HasFlag(NativeClassTail.ControlRigClass) && tails.HasFlag(NativeClassTail.RigVMClass) && asset.GetCustomVersion(UE5MainStreamGuid) >= RigVMGeneratedClass;
            if (controlRigVM || rigVMClassVM)
            {
                long fileOffset = export.SerialOffset + export.SerialSize - export.Extras.Length + stream.Position;
                long start = stream.Position;
                var rest = reader.ReadBytes((int)(stream.Length - stream.Position));
                data.EmbeddedVM = RigVMData.Parse(asset, rest, fileOffset, false, out int vmLength);
                stream.Position = start;
                data.EmbeddedVMBytes = reader.ReadBytes(vmLength);
                // FRigVMGraphFunctionStore: the public functions (private ones are written only for reference collection);
                // the Control Rig class stores it from StoreFunctionsInGeneratedClass
                if (rigVMClassVM || asset.GetCustomVersion(ControlRigGuid) >= StoreFunctionsInGeneratedClass)
                {
                    long storeStart = stream.Position;
                    data.GraphFunctions = RigVMData.ReadGraphFunctions(reader);
                    long storeEnd = stream.Position;
                    stream.Position = storeStart;
                    data.GraphFunctionStoreBytes = reader.ReadBytes((int)(storeEnd - storeStart));
                }
            }
            if (tails.HasFlag(NativeClassTail.PCGComponent) && asset.GetCustomVersion(PCGGuid) >= DynamicTrackingKeysSerializedInComponent)
            {
                int keys = reader.ReadInt32();
                if (keys < 0 || keys > stream.Length - stream.Position) return;
                data.PCGTrackedKeys = new List<FPCGTrackedKey>(keys);
                var keyStruct = FName.DefineDummy(asset, "PCGSelectionKey");
                for (int k = 0; k < keys; k++)
                {
                    var entry = new FPCGTrackedKey { Key = new List<PropertyData>() };
                    for (PropertyData p; (p = MainSerializer.Read(reader, new AncestryInfo(), keyStruct, null, null, true)) != null;) entry.Key.Add(p);
                    int settings = reader.ReadInt32();
                    if (settings < 0 || settings > stream.Length - stream.Position) return;
                    entry.Settings = new List<FPCGTrackedSettings>(settings);
                    for (int s = 0; s < settings; s++) entry.Settings.Add(new FPCGTrackedSettings { Settings = new FSoftObjectPath(reader), bCulled = reader.ReadBooleanInt() });
                    data.PCGTrackedKeys.Add(entry);
                }
            }
            // cooked collision data and level-set implicit objects aren't decoded: such a tail keeps its bytes
            if (tails.HasFlag(NativeClassTail.BodySetup))
            {
                data.BodySetupGuid = reader.ReadGuid();
                data.BodySetupIsCooked = reader.ReadBooleanInt();
                if (data.BodySetupIsCooked == true) return;
                if (asset.GetCustomVersion(FortniteMainGuid) >= LevelsetSerializationSupportForBodySetup && asset.GetCustomVersion(FortniteReleaseGuid) < DisableLevelset_v14_10)
                {
                    data.BodySetupImplicitObjects = reader.ReadInt32();
                    if (data.BodySetupImplicitObjects != 0) return;
                }
            }
            if (stream.Position != stream.Length) { export.NativeTailFailure = $"{stream.Length - stream.Position} bytes after the {tails} tails"; return; }
            export.NativeTails = data;
            export.Extras = Array.Empty<byte>();
        }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException)
        {
            export.NativeTailFailure = $"{tails} tails: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}";
        }
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
        if (data.SkyAtmosphereLightingGuid != null) writer.Write(data.SkyAtmosphereLightingGuid.Value);
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
        if (data.LegacyGeneratedClassIsAuthoritative != null) writer.WriteBooleanInt(data.LegacyGeneratedClassIsAuthoritative.Value);
        if (data.NiagaraTextureStreamData != null) { writer.Write(data.NiagaraTextureStreamData.Length); writer.Write(data.NiagaraTextureStreamData); }
        if (data.WorldPersistentLevel != null)
        {
            data.WorldPersistentLevel.Write(writer);
            foreach (var list in new[] { data.WorldExtraReferencedObjects, data.WorldStreamingLevels }) { writer.Write(list.Count); foreach (var i in list) i.Write(writer); }
        }
        if (data.WorldPartitionIsCooked != null)
        {
            writer.WriteBooleanInt(data.WorldPartitionIsCooked.Value);
            if (data.WorldPartitionIsCooked.Value) data.WorldPartitionStreamingPolicy.Write(writer);
        }
        if (data.MaterialInterfaceSavedCachedExpressionData != null) writer.WriteBooleanInt(data.MaterialInterfaceSavedCachedExpressionData.Value);
        if (data.MaterialInstanceSavedCachedData != null) writer.WriteBooleanInt(data.MaterialInstanceSavedCachedData.Value);
        if (data.MaterialInstanceInlineShaderMaps != null) writer.Write(data.MaterialInstanceInlineShaderMaps.Value);
        if (data.MaterialSavedCachedExpressionData != null) writer.WriteBooleanInt(data.MaterialSavedCachedExpressionData.Value);
        if (data.RedirectorDestination != null) data.RedirectorDestination.Write(writer);
        if (data.StaticMeshMaterials != null) WriteStaticMesh(writer, data);
        if (data.SkeletonGuid != null) writer.Write(data.SkeletonGuid.Value);
        if (data.AnimSequenceStripFlags != null)
        {
            writer.Write(data.AnimSequenceStripFlags);
            if (data.AnimSequenceRawTracks != null) writer.Write(data.AnimSequenceRawTracks);
            if (data.AnimSequenceSourceRawTracks != null) writer.Write(data.AnimSequenceSourceRawTracks);
            if (data.AnimSequenceLegacyCompressedData != null) { writer.Write(data.AnimSequenceLegacyCompressedData.Length); writer.Write(data.AnimSequenceLegacyCompressedData); }
            if (data.AnimSequenceSerializeCompressedData != null) writer.WriteBooleanInt(data.AnimSequenceSerializeCompressedData.Value);
        }
        if (data.ControlRigPhysicsSolvers != null)
        {
            writer.Write(data.ControlRigPhysicsSolvers.Count);
            foreach (var kv in data.ControlRigPhysicsSolvers) { writer.Write(kv.Key); writer.Write(kv.Value); }
        }
        if (data.RigVMPropertyPaths != null)
        {
            writer.Write(data.RigVMPropertyPaths.Count);
            foreach (var d in data.RigVMPropertyPaths) { writer.Write(d.PropertyIndex); writer.Write(d.HeadCPPType); writer.Write(d.SegmentPath); }
            writer.Write(data.RigVMMemoryType.Value);
        }
        if (data.EmbeddedVMBytes != null) writer.Write(data.EmbeddedVMBytes);
        if (data.GraphFunctionStoreBytes != null) writer.Write(data.GraphFunctionStoreBytes);
        if (data.PCGTrackedKeys != null)
        {
            writer.Write(data.PCGTrackedKeys.Count);
            foreach (var entry in data.PCGTrackedKeys)
            {
                foreach (var p in entry.Key) MainSerializer.Write(p, writer, true);
                writer.Write(new FName(writer.Asset, "None"));
                writer.Write(entry.Settings.Count);
                foreach (var s in entry.Settings) { s.Settings.Write(writer); writer.WriteBooleanInt(s.bCulled); }
            }
        }
        if (data.BodySetupGuid != null)
        {
            writer.Write(data.BodySetupGuid.Value);
            writer.WriteBooleanInt(data.BodySetupIsCooked.Value);
            if (data.BodySetupImplicitObjects != null) writer.Write(data.BodySetupImplicitObjects.Value);
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
            if (schema != null && !string.IsNullOrEmpty(schema.SuperType) && !string.IsNullOrEmpty(schema.SuperTypeModulePath))
            {
                string module = schema.SuperTypeModulePath;
                native = (module.StartsWith("/Script/") ? module : "/Script/" + module, schema.SuperType);
                continue;
            }
            // without a schema, a project class's parent comes from its declaration; the parent's module isn't declared,
            // so an engine class reached that way is matched by name
            if (!ProjectNativeParents.TryGetValue(native.Class, out var parent)) return null;
            if (TailsByClassName.Value.TryGetValue(parent, out var byName)) return byName;
            native = (null, parent);
        }
        return null;
    }

    /// <summary>
    /// Reflected class name to reflected parent name for project and plugin C++ classes that no mappings describe,
    /// from their UCLASS declarations. Filled by the caller.
    /// </summary>
    public static readonly Dictionary<string, string> ProjectNativeParents = new();

    static readonly Lazy<Dictionary<string, NativeClassTail>> TailsByClassName = new(() =>
    {
        var byName = new Dictionary<string, NativeClassTail>();
        foreach (var kv in NativeClassTailTable.Tails) byName.TryAdd(kv.Key.Class, kv.Value);
        return byName;
    });

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
                var other = new UAsset(path, EngineVersion.UNKNOWN, asset.Mappings, CustomSerializationFlags.SkipParsingBytecode | CustomSerializationFlags.SkipPreloadDependencyLoading);
                var classExport = other.Exports.OfType<StructExport>().FirstOrDefault(e => e.ObjectName?.ToString() == name);
                if (classExport != null) result = NativeBaseOf(other, classExport.SuperStruct, depth + 1);
            }
        }
        catch (Exception) { }
        BlueprintNativeBase[key] = result;
        return result;
    }
}
