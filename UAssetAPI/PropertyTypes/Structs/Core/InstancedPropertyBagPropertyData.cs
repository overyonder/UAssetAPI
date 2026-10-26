using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>One property of a property bag, as PropertyBag.cpp operator&lt;&lt;(FArchive&amp;, FPropertyBagPropertyDesc&amp;) writes it.</summary>
public class FPropertyBagPropertyDesc
{
    [JsonProperty] public FPackageIndex ValueTypeObject;
    [JsonProperty] public Guid ID;
    [JsonProperty] public FName Name;
    /// <summary>EPropertyBagPropertyType.</summary>
    [JsonProperty] public byte ValueType;
    /// <summary>EPropertyBagContainerType per nesting level, outermost first.</summary>
    [JsonProperty] public byte[] ContainerTypes = [];
    [JsonProperty] public List<KeyValuePair<FName, FString>> MetaData;
    [JsonProperty] public FPackageIndex MetaClass;
    [JsonProperty] public ulong PropertyFlags;
    [JsonProperty] public byte KeyType;
    [JsonProperty] public FPackageIndex KeyTypeObject;
}

/// <summary>
/// FInstancedPropertyBag (PropertyBag.cpp FInstancedPropertyBag::Serialize): a version-dependent table of property
/// descriptors, then the bag's values serialized with the struct those descriptors define. Tagged packages carry each
/// value's type in its tag, so the values are read as ordinary tagged properties.
/// </summary>
public class InstancedPropertyBagPropertyData : PropertyData<List<PropertyData>>
{
    // FPropertyBagCustomVersion: ContainerTypes 1, NestedContainerTypes 2, MetaClass 3, PropertyFlags 4, KeyTypes 5.
    static readonly Guid PropertyBagGuid = UAPUtils.GUID(0x134A157E, 0xD5E249A3, 0x8D4E843C, 0x98FE9E31);

    /// <summary>The obsolete per-bag version byte, present before FPropertyBagCustomVersion::ContainerTypes.</summary>
    [JsonProperty] public byte? LegacyVersion;
    [JsonProperty] public bool HasData;
    [JsonProperty] public List<FPropertyBagPropertyDesc> PropertyDescs;

    public InstancedPropertyBagPropertyData(FName name) : base(name) { }
    public InstancedPropertyBagPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("InstancedPropertyBag");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    static int BagVersion(UAsset asset) => Math.Max(0, asset.GetCustomVersion(PropertyBagGuid));

    bool HasSerialSize(UAsset asset) => BagVersion(asset) >= 1 || LegacyVersion >= 1;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0,
        PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) ReadEndPropertyTag(reader);
        if (reader.Asset.HasUnversionedProperties)
            throw new NotSupportedException("Property bag values in unversioned packages need the bag's generated struct layout");

        int version = BagVersion(reader.Asset);
        if (version < 1) LegacyVersion = reader.ReadByte();
        HasData = reader.ReadBooleanInt();
        Value = new List<PropertyData>();
        if (!HasData) return;

        int count = reader.ReadInt32();
        if (count < 0 || count > (reader.BaseStream.Length - reader.BaseStream.Position)) throw new FormatException($"Invalid property bag descriptor count {count}");
        PropertyDescs = new List<FPropertyBagPropertyDesc>(count);
        for (int i = 0; i < count; i++)
        {
            var desc = new FPropertyBagPropertyDesc
            {
                ValueTypeObject = new FPackageIndex(reader),
                ID = reader.ReadGuid(),
                Name = reader.ReadFName(),
                ValueType = reader.ReadByte(),
            };
            if (version >= 2) desc.ContainerTypes = reader.ReadBytes(reader.ReadByte());
            else if (version >= 1) { byte single = reader.ReadByte(); desc.ContainerTypes = single == 0 ? [] : [single]; }
            if (reader.ReadBooleanInt())
            {
                int metaCount = reader.ReadInt32();
                desc.MetaData = new List<KeyValuePair<FName, FString>>(metaCount);
                for (int m = 0; m < metaCount; m++) desc.MetaData.Add(new(reader.ReadFName(), reader.ReadFString()));
                if (version >= 3) desc.MetaClass = new FPackageIndex(reader);
            }
            if (version >= 4) desc.PropertyFlags = reader.ReadUInt64();
            if (version >= 5) { desc.KeyType = reader.ReadByte(); desc.KeyTypeObject = new FPackageIndex(reader); }
            PropertyDescs.Add(desc);
        }

        long end = -1;
        if (HasSerialSize(reader.Asset)) { int size = reader.ReadInt32(); end = reader.BaseStream.Position + size; }
        PropertyData bit;
        while ((bit = MainSerializer.Read(reader, null, FName.DefineDummy(reader.Asset, "PropertyBag"), null, null, true)) != null) Value.Add(bit);
        if (end >= 0 && reader.BaseStream.Position != end) throw new FormatException($"Property bag values end at {reader.BaseStream.Position}, expected {end}");
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader,
        PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        int version = BagVersion(writer.Asset);
        if (version < 1) writer.Write(LegacyVersion ?? 0);
        writer.WriteBooleanInt(HasData);
        if (HasData)
        {
            writer.Write(PropertyDescs.Count);
            foreach (var desc in PropertyDescs)
            {
                desc.ValueTypeObject.Write(writer);
                writer.Write(desc.ID);
                writer.Write(desc.Name);
                writer.Write(desc.ValueType);
                if (version >= 2) { writer.Write((byte)desc.ContainerTypes.Length); writer.Write(desc.ContainerTypes); }
                else if (version >= 1) writer.Write(desc.ContainerTypes.Length > 0 ? desc.ContainerTypes[0] : (byte)0);
                writer.WriteBooleanInt(desc.MetaData != null);
                if (desc.MetaData != null)
                {
                    writer.Write(desc.MetaData.Count);
                    foreach (var kv in desc.MetaData) { writer.Write(kv.Key); writer.Write(kv.Value); }
                    if (version >= 3) (desc.MetaClass ?? new FPackageIndex()).Write(writer);
                }
                if (version >= 4) writer.Write(desc.PropertyFlags);
                if (version >= 5) { writer.Write(desc.KeyType); (desc.KeyTypeObject ?? new FPackageIndex()).Write(writer); }
            }

            long sizePos = -1;
            if (HasSerialSize(writer.Asset)) { sizePos = writer.BaseStream.Position; writer.Write(0); }
            long valuesStart = writer.BaseStream.Position;
            foreach (var value in Value) MainSerializer.Write(value, writer, true);
            writer.Write(new FName(writer.Asset, "None"));
            if (sizePos >= 0)
            {
                long end = writer.BaseStream.Position;
                writer.BaseStream.Position = sizePos;
                writer.Write((int)(end - valuesStart));
                writer.BaseStream.Position = end;
            }
        }
        return (int)(writer.BaseStream.Position - start);
    }
}
