using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// CoreUObject StructUtils/InstancedStructContainer.cpp FInstancedStructContainer::Serialize: a version byte, the item count,
/// each item's script struct, then each item's serial size and its tagged properties.
/// </summary>
public class InstancedStructContainerPropertyData : PropertyData<List<InstancedStructContainerPropertyData.Item>>
{
    public class Item
    {
        [JsonProperty] public FPackageIndex Struct;
        [JsonProperty] public StructPropertyData Value;
        /// <summary>An item whose struct is null: the engine skips its serialized bytes.</summary>
        [JsonProperty] public byte[] SkippedBytes;
    }

    [JsonProperty] public byte Version;

    public InstancedStructContainerPropertyData(FName name) : base(name) { }
    public InstancedStructContainerPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("InstancedStructContainer");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);

        Version = reader.ReadByte();
        if (Version > 0) throw new FormatException($"Unknown FInstancedStructContainer version {Version}");
        int n = reader.ReadInt32();
        if (n < 0 || (long)n * 8 > reader.BaseStream.Length - reader.BaseStream.Position) throw new FormatException($"Invalid FInstancedStructContainer item count {n}");
        Value = new List<Item>(n);
        for (int i = 0; i < n; i++) Value.Add(new Item { Struct = new FPackageIndex(reader) });
        foreach (var item in Value)
        {
            int size = reader.ReadInt32();
            long end = reader.BaseStream.Position + size;
            if (size < 0 || end > reader.BaseStream.Length) throw new FormatException($"Invalid FInstancedStructContainer item size {size}");
            if (item.Struct.IsNull()) { item.SkippedBytes = reader.ReadBytes(size); continue; }
            item.Value = new StructPropertyData(FName.DefineDummy(reader.Asset, "InstancedStructContainer"));
            var unversionedHeader = new FUnversionedHeader(reader);
            item.Value.UnversionedHeader = reader.Asset.HasUnversionedProperties ? unversionedHeader : null;
            FName parentName = Export.GetClassTypeForAncestry(item.Struct, reader.Asset, out FName parentModulePath);
            PropertyData bit;
            while ((bit = MainSerializer.Read(reader, null, parentName, parentModulePath, unversionedHeader, true)) != null) item.Value.Value.Add(bit);
            if (reader.BaseStream.Position != end)
                throw new FormatException($"FInstancedStructContainer item {parentName} read {reader.BaseStream.Position - end + size} of {size} bytes");
        }
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);

        var start = writer.BaseStream.Position;
        writer.Write(Version);
        writer.Write(Value.Count);
        foreach (var item in Value) writer.Write(item.Struct.Index);
        foreach (var item in Value)
        {
            var sizeAt = writer.BaseStream.Position;
            writer.Write(0);
            if (item.Struct.IsNull()) writer.Write(item.SkippedBytes ?? Array.Empty<byte>());
            else
            {
                FName parentName = Export.GetClassTypeForAncestry(item.Struct, writer.Asset, out FName parentModulePath);
                item.Value.Ancestry.Initialize(null, parentName, parentModulePath);
                var data = item.Value.Value;
                foreach (PropertyData current in data) current.Ancestry.Initialize(null, parentName, parentModulePath);
                MainSerializer.GenerateUnversionedHeader(ref data, parentName, parentModulePath, writer.Asset)?.Write(writer);
                foreach (var current in data) MainSerializer.Write(current, writer, true);
                if (!writer.Asset.HasUnversionedProperties) writer.Write(new FName(writer.Asset, "None"));
            }
            var end = writer.BaseStream.Position;
            writer.BaseStream.Position = sizeAt;
            writer.Write((int)(end - sizeAt - sizeof(int)));
            writer.BaseStream.Position = end;
        }
        return (int)(writer.BaseStream.Position - start);
    }
}
