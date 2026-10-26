using System.Collections.Generic;
using UAssetAPI.CustomVersions;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// URL structure.
/// </summary>
public struct FURL
{
    // Protocol, i.e. "unreal" or "http".
    public FString Protocol;
    // Optional hostname, i.e. "204.157.115.40" or "unreal.epicgames.com", blank if local.
    public FString Host;
    // Optional host port.
    public int Port;
    public int Valid;
    // Map name, i.e. "SkyCity", default is "Entry".
    public FString Map;
    // Options.
    public List<FString> Op;
    // Portal to enter through, default is "".
    public FString Portal;

    public FURL(AssetBinaryReader reader)
    {
        Protocol = reader.ReadFString();
        Host = reader.ReadFString();
        Map = reader.ReadFString();
        Portal = reader.ReadFString();
        var len = reader.ReadInt32();
        Op = new List<FString>(len);
        for (int i = 0; i < len; i++)
        {
            Op.Add(reader.ReadFString());
        }
        Port = reader.ReadInt32();
        Valid = reader.ReadInt32();
    }

    public int Write(AssetBinaryWriter writer)
    {

        var offset = writer.BaseStream.Position;
        writer.Write(Protocol);
        writer.Write(Host);
        writer.Write(Map);
        writer.Write(Portal);
        writer.Write(Op.Count);
        for (int i = 0; i < Op.Count; i++)
        {
            writer.Write(Op[i]);
        }
        writer.Write(Port);
        writer.Write(Valid);
        return (int)(writer.BaseStream.Position - offset);
    }
}

/// <summary>Level.h FPrecomputedVisibilityCell: the cell's minimum corner and where its data sits in a chunk.</summary>
public class FPrecomputedVisibilityCell
{
    public FVector Min;
    public ushort ChunkIndex;
    public ushort DataOffset;
}

/// <summary>Level.h FCompressedVisibilityChunk.</summary>
public class FCompressedVisibilityChunk
{
    public bool bCompressed;
    public int UncompressedSize;
    public byte[] Data;
}

/// <summary>Level.h FPrecomputedVisibilityBucket.</summary>
public class FPrecomputedVisibilityBucket
{
    public int CellDataSize;
    public List<FPrecomputedVisibilityCell> Cells;
    public List<FCompressedVisibilityChunk> CellDataChunks;
}

/// <summary>Level.cpp operator&lt;&lt;(FArchive&amp;, FPrecomputedVisibilityHandler&amp;).</summary>
public class FPrecomputedVisibilityHandler
{
    public FVector2D CellBucketOriginXY;
    public float CellSizeXY;
    public float CellSizeZ;
    public int CellBucketSizeXY;
    public int NumCellBuckets;
    public List<FPrecomputedVisibilityBucket> CellBuckets;
}

/// <summary>Level.cpp operator&lt;&lt;(FArchive&amp;, FPrecomputedVolumeDistanceField&amp;); Data holds FColor values as stored (B, G, R, A bytes).</summary>
public class FPrecomputedVolumeDistanceField
{
    public float VolumeMaxDistance;
    public TBox<FVector> VolumeBox;
    public int VolumeSizeX;
    public int VolumeSizeY;
    public int VolumeSizeZ;
    public List<uint> Data;
}

/// <summary>
/// Level.cpp ULevel::Serialize, after the tagged properties. Branches that only packages from before 4.15 or so
/// contain (texture streaming data, precomputed light volumes, world tile info) stop the read there, and the rest stays
/// in Extras.
/// </summary>
public class LevelExport : NormalExport
{
    static readonly System.Guid RenderingGuid = UAPUtils.GUID(0x12F88B9F, 0x88754AFC, 0xA67CD90C, 0x383ABD29);
    const int RemovedTextureStreamingLevelData = 3, MapBuildDataSeparatePackage = 9;

    // Owner of TTransArray<AActor> Actors
    public FPackageIndex Owner;
    public List<FPackageIndex> Actors;
    public FURL URL;
    public FPackageIndex Model;
    public List<FPackageIndex> ModelComponents;
    /// <summary>The level blueprint, stored in packages that keep editor-only data.</summary>
    public FPackageIndex LevelScriptBlueprint;
    public FPackageIndex LevelScriptActor;
    public FPackageIndex NavListStart;
    public FPackageIndex NavListEnd;
    public FPrecomputedVisibilityHandler PrecomputedVisibilityHandler;
    public FPrecomputedVolumeDistanceField PrecomputedVolumeDistanceField;
    /// <summary>Actor folder objects by GUID, stored when the level uses actor folders and not external actors.</summary>
    public List<KeyValuePair<System.Guid, FPackageIndex>> ActorFolders;
    /// <summary>Set when a legacy branch stopped the read; the bytes from there on are in Extras.</summary>
    public bool LegacyTailUnread;

    public LevelExport(Export super) : base(super) { }

    public LevelExport(UAsset asset, byte[] extras) : base(asset, extras) { }

    public LevelExport() { }

    bool BoolProperty(string name) => Data?.Find(p => p.Name?.ToString() == name) is PropertyTypes.Objects.BoolPropertyData b && b.Value;

    bool StoresLevelScriptBlueprint(UAsset asset) => !asset.IsFilterEditorOnly || asset.ObjectVersion < ObjectVersion.VER_UE4_EDITORONLY_BLUEPRINTS;

    bool StoresActorFolders(UAsset asset) =>
        !asset.IsFilterEditorOnly && asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() >= FUE5ReleaseStreamObjectVersion.AddLevelActorFolders &&
        BoolProperty("bUseActorFolders") && !BoolProperty("bUseExternalActors") && !asset.PackageFlags.HasFlag(EPackageFlags.PKG_Cooked);

    public override void Read(AssetBinaryReader reader, int nextStarting)
    {
        base.Read(reader, nextStarting);
        var asset = reader.Asset;

        if (asset.GetCustomVersion<FReleaseObjectVersion>() < FReleaseObjectVersion.LevelTransArrayConvertedToTArray)
            Owner = new FPackageIndex(reader);
        Actors = ReadIndices(reader);
        URL = new FURL(reader);
        Model = new FPackageIndex(reader);
        ModelComponents = ReadIndices(reader);
        if (StoresLevelScriptBlueprint(asset)) LevelScriptBlueprint = new FPackageIndex(reader);
        LevelScriptActor = new FPackageIndex(reader);
        if (asset.GetCustomVersion(RenderingGuid) < RemovedTextureStreamingLevelData) { LegacyTailUnread = true; return; }
        NavListStart = new FPackageIndex(reader);
        NavListEnd = new FPackageIndex(reader);
        if (asset.GetCustomVersion(RenderingGuid) < MapBuildDataSeparatePackage) { LegacyTailUnread = true; return; }
        PrecomputedVisibilityHandler = ReadVisibility(reader);
        PrecomputedVolumeDistanceField = ReadDistanceField(reader);
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_WORLD_LEVEL_INFO && asset.ObjectVersion < ObjectVersion.VER_UE4_WORLD_LEVEL_INFO_UPDATED) { LegacyTailUnread = true; return; }
        if (StoresActorFolders(asset))
        {
            int n = reader.ReadInt32();
            ActorFolders = new List<KeyValuePair<System.Guid, FPackageIndex>>(n);
            for (int i = 0; i < n; i++) ActorFolders.Add(new(reader.ReadGuid(), new FPackageIndex(reader)));
        }
    }

    public override void Write(AssetBinaryWriter writer)
    {
        base.Write(writer);
        var asset = writer.Asset;

        if (asset.GetCustomVersion<FReleaseObjectVersion>() < FReleaseObjectVersion.LevelTransArrayConvertedToTArray)
            Owner.Write(writer);
        WriteIndices(writer, Actors);
        URL.Write(writer);
        Model.Write(writer);
        WriteIndices(writer, ModelComponents);
        if (StoresLevelScriptBlueprint((UAsset)asset)) LevelScriptBlueprint.Write(writer);
        LevelScriptActor.Write(writer);
        if (NavListStart == null) return;
        NavListStart.Write(writer);
        NavListEnd.Write(writer);
        if (PrecomputedVisibilityHandler == null) return;
        WriteVisibility(writer, PrecomputedVisibilityHandler);
        WriteDistanceField(writer, PrecomputedVolumeDistanceField);
        if (ActorFolders != null)
        {
            writer.Write(ActorFolders.Count);
            foreach (var kv in ActorFolders) { writer.Write(kv.Key); kv.Value.Write(writer); }
        }
    }

    static List<FPackageIndex> ReadIndices(AssetBinaryReader reader)
    {
        int n = reader.ReadInt32();
        var list = new List<FPackageIndex>(n);
        for (int i = 0; i < n; i++) list.Add(new FPackageIndex(reader));
        return list;
    }

    static void WriteIndices(AssetBinaryWriter writer, List<FPackageIndex> list)
    {
        writer.Write(list.Count);
        foreach (var index in list) index.Write(writer);
    }

    static FPrecomputedVisibilityHandler ReadVisibility(AssetBinaryReader reader)
    {
        var h = new FPrecomputedVisibilityHandler
        {
            CellBucketOriginXY = new FVector2D(reader),
            CellSizeXY = reader.ReadSingle(),
            CellSizeZ = reader.ReadSingle(),
            CellBucketSizeXY = reader.ReadInt32(),
            NumCellBuckets = reader.ReadInt32()
        };
        int buckets = reader.ReadInt32();
        h.CellBuckets = new List<FPrecomputedVisibilityBucket>(buckets);
        for (int i = 0; i < buckets; i++)
        {
            var b = new FPrecomputedVisibilityBucket { CellDataSize = reader.ReadInt32() };
            int cells = reader.ReadInt32();
            b.Cells = new List<FPrecomputedVisibilityCell>(cells);
            for (int c = 0; c < cells; c++) b.Cells.Add(new FPrecomputedVisibilityCell { Min = new FVector(reader), ChunkIndex = reader.ReadUInt16(), DataOffset = reader.ReadUInt16() });
            int chunks = reader.ReadInt32();
            b.CellDataChunks = new List<FCompressedVisibilityChunk>(chunks);
            for (int c = 0; c < chunks; c++)
                b.CellDataChunks.Add(new FCompressedVisibilityChunk { bCompressed = reader.ReadBooleanInt(), UncompressedSize = reader.ReadInt32(), Data = reader.ReadBytes(reader.ReadInt32()) });
            h.CellBuckets.Add(b);
        }
        return h;
    }

    static void WriteVisibility(AssetBinaryWriter writer, FPrecomputedVisibilityHandler h)
    {
        h.CellBucketOriginXY.Write(writer);
        writer.Write(h.CellSizeXY);
        writer.Write(h.CellSizeZ);
        writer.Write(h.CellBucketSizeXY);
        writer.Write(h.NumCellBuckets);
        writer.Write(h.CellBuckets.Count);
        foreach (var b in h.CellBuckets)
        {
            writer.Write(b.CellDataSize);
            writer.Write(b.Cells.Count);
            foreach (var c in b.Cells) { c.Min.Write(writer); writer.Write(c.ChunkIndex); writer.Write(c.DataOffset); }
            writer.Write(b.CellDataChunks.Count);
            foreach (var c in b.CellDataChunks) { writer.WriteBooleanInt(c.bCompressed); writer.Write(c.UncompressedSize); writer.Write(c.Data.Length); writer.Write(c.Data); }
        }
    }

    static FPrecomputedVolumeDistanceField ReadDistanceField(AssetBinaryReader reader)
    {
        var d = new FPrecomputedVolumeDistanceField
        {
            VolumeMaxDistance = reader.ReadSingle(),
            VolumeBox = new TBox<FVector>(reader, () => new FVector(reader)),
            VolumeSizeX = reader.ReadInt32(),
            VolumeSizeY = reader.ReadInt32(),
            VolumeSizeZ = reader.ReadInt32()
        };
        int n = reader.ReadInt32();
        d.Data = new List<uint>(n);
        for (int i = 0; i < n; i++) d.Data.Add(reader.ReadUInt32());
        return d;
    }

    static void WriteDistanceField(AssetBinaryWriter writer, FPrecomputedVolumeDistanceField d)
    {
        writer.Write(d.VolumeMaxDistance);
        d.VolumeBox.Write(writer, v => v.Write(writer));
        writer.Write(d.VolumeSizeX);
        writer.Write(d.VolumeSizeY);
        writer.Write(d.VolumeSizeZ);
        writer.Write(d.Data.Count);
        foreach (var c in d.Data) writer.Write(c);
    }
}
