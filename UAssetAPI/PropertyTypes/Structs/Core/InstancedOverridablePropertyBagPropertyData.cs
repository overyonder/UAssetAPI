using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// GameplayCameras StructUtils/OverridablePropertyBag.cpp FInstancedOverridablePropertyBag::Serialize: from
/// FOverridablePropertyBagCustomVersion::FixSerializer, the FInstancedPropertyBag then the overridden property IDs; before
/// it, the struct's tagged properties.
/// </summary>
public class InstancedOverridablePropertyBagPropertyData : InstancedPropertyBagPropertyData
{
    static readonly Guid OverridablePropertyBagGuid = UAPUtils.GUID(0x5426C227, 0x4B3145B2, 0x9B9BED1F, 0x327FB126);
    const int FixSerializer = 1;

    [JsonProperty] public List<Guid> OverridenPropertyIDs;
    /// <summary>The tagged properties of a bag saved before FixSerializer.</summary>
    [JsonProperty] public StructPropertyData TaggedBeforeFixSerializer;

    public InstancedOverridablePropertyBagPropertyData(FName name) : base(name) { }
    public InstancedOverridablePropertyBagPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("InstancedOverridablePropertyBag");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (reader.Asset.GetCustomVersion(OverridablePropertyBagGuid) < FixSerializer)
        {
            if (includeHeader) this.ReadEndPropertyTag(reader);
            TaggedBeforeFixSerializer = new StructPropertyData(Name, FName.DefineDummy(reader.Asset, CurrentPropertyType.Value));
            TaggedBeforeFixSerializer.Ancestry.Initialize(Ancestry, Name);
            TaggedBeforeFixSerializer.Read(reader, false, 1, 0, PropertySerializationContext.StructFallback);
            return;
        }
        base.Read(reader, includeHeader, leng1, leng2, serializationContext);
        int n = reader.ReadInt32();
        if (n < 0 || (long)n * 16 > reader.BaseStream.Length - reader.BaseStream.Position) throw new FormatException($"Invalid overridden property ID count {n}");
        OverridenPropertyIDs = new List<Guid>(n);
        for (int i = 0; i < n; i++) OverridenPropertyIDs.Add(reader.ReadGuid());
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (TaggedBeforeFixSerializer != null)
        {
            if (includeHeader) this.WriteEndPropertyTag(writer);
            return TaggedBeforeFixSerializer.Write(writer, false, PropertySerializationContext.StructFallback);
        }
        int size = base.Write(writer, includeHeader, serializationContext);
        writer.Write(OverridenPropertyIDs.Count);
        foreach (var id in OverridenPropertyIDs) writer.Write(id);
        return size + 4 + 16 * OverridenPropertyIDs.Count;
    }
}
