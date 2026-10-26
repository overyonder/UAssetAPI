namespace UAssetAPI.ExportTypes;

/// <summary>Class.cpp UScriptStruct::Serialize: the struct's non-computed flags after the UStruct data.</summary>
public class ScriptStructExport : StructExport
{
    public uint StructFlags;

    public ScriptStructExport(Export super) : base(super) { }

    public ScriptStructExport(UAsset asset, byte[] extras) : base(asset, extras) { }

    public ScriptStructExport() { }

    public override void Read(AssetBinaryReader reader, int nextStarting)
    {
        base.Read(reader, nextStarting);
        StructFlags = reader.ReadUInt32();
    }

    public override void Write(AssetBinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(StructFlags);
    }
}
