using System;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// Animation/AnimTypes.h operator&lt;&lt;(FArchive&amp;, FRawAnimSequenceTrack&amp;): position, rotation and (from
/// VER_UE4_ANIM_SUPPORT_NONUNIFORM_SCALE_ANIMATION) scale keys, each bulk serialized as element size, count and bytes.
/// The keys are kept as stored.
/// </summary>
public class RawAnimSequenceTrackPropertyData : PropertyData<byte[][]>
{
    public RawAnimSequenceTrackPropertyData() { }
    public RawAnimSequenceTrackPropertyData(FName name) : base(name) { }
    private static readonly FString CurrentPropertyType = new("RawAnimSequenceTrack");
    public override FString PropertyType => CurrentPropertyType;
    public override bool HasCustomStructSerialization => true;

    /// <summary>Per key array, its element size as stored.</summary>
    public int[] ElementSizes;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) ReadEndPropertyTag(reader);
        int arrays = reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_ANIM_SUPPORT_NONUNIFORM_SCALE_ANIMATION ? 3 : 2;
        Value = new byte[arrays][];
        ElementSizes = new int[arrays];
        for (int a = 0; a < arrays; a++)
        {
            int size = reader.ReadInt32(), count = reader.ReadInt32();
            if (size < 0 || count < 0 || (long)size * count > reader.BaseStream.Length - reader.BaseStream.Position) throw new FormatException("Invalid raw key array");
            ElementSizes[a] = size;
            Value[a] = reader.ReadBytes(size * count);
        }
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        for (int a = 0; a < Value.Length; a++)
        {
            writer.Write(ElementSizes[a]);
            writer.Write(ElementSizes[a] == 0 ? 0 : Value[a].Length / ElementSizes[a]);
            writer.Write(Value[a]);
        }
        return checked((int)(writer.BaseStream.Position - start));
    }
}
