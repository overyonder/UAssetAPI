using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

public class VectorNetQuantizePropertyData : StructPropertyData
{
    public VectorNetQuantizePropertyData(FName name, FName forcedType) : base(name, forcedType)
    {
        Value.Add(new VectorPropertyData(name)); //backup for isZero
    }

    public VectorNetQuantizePropertyData(FName name) : base(name)
    {
        Value.Add(new VectorPropertyData(name));
    }

    public VectorNetQuantizePropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector_NetQuantize");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    // NetSerialization.h gives FVector_NetQuantize and its variants net serializers only, and STRUCT_Immutable isn't inherited
    // from FVector (STRUCT_Inherit is HasInstancedReference | Atomic), so UScriptStruct::SerializeItem saves them as tagged
    // properties, X, Y and Z
    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        Value = [];
        StructType = FName.DefineDummy(reader.Asset, PropertyType);
        base.Read(reader, includeHeader, 1, leng2, PropertySerializationContext.StructFallback);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        StructType = FName.DefineDummy(writer.Asset, PropertyType);
        return base.Write(writer, includeHeader, PropertySerializationContext.StructFallback);
    }
}

public class VectorNetQuantizeNormalPropertyData : VectorNetQuantizePropertyData
{
    public VectorNetQuantizeNormalPropertyData(FName name) : base(name) { }

    public VectorNetQuantizeNormalPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector_NetQuantizeNormal");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}

public class VectorNetQuantize10PropertyData : VectorNetQuantizePropertyData
{
    public VectorNetQuantize10PropertyData(FName name) : base(name) { }

    public VectorNetQuantize10PropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector_NetQuantize10");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}

public class VectorNetQuantize100PropertyData : VectorNetQuantizePropertyData
{
    public VectorNetQuantize100PropertyData(FName name) : base(name) { }

    public VectorNetQuantize100PropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector_NetQuantize100");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}
