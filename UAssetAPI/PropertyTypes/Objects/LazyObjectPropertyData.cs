using System;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Objects;

/// <summary>
/// A lazy object pointer (CoreUObject PropertyLazyObjectPtr.cpp FLazyObjectProperty::SerializeItem): the referenced object's
/// unique ID, an FUniqueObjectGuid (LazyObjectPtr.h), so a GUID.
/// </summary>
public class LazyObjectPropertyData : PropertyData<Guid>
{
    public LazyObjectPropertyData(FName name) : base(name) { }

    public LazyObjectPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("LazyObjectProperty");
    public override FString PropertyType { get { return CurrentPropertyType; } }
    public override object DefaultValue { get { return Guid.Empty; } }

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        Value = reader.ReadGuid();
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        writer.Write(Value);
        return 16;
    }

    public override string ToString() => Value.ToString();

    public override void FromString(string[] d, UAsset asset)
    {
        Value = Guid.TryParse(d[0], out var g) ? g : Guid.Empty;
    }
}
