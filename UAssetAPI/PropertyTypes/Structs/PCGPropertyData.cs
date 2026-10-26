using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>PCG Data/PCGDataPtrWrapper.cpp FPCGDataPtrWrapper::Serialize: the wrapped UPCGData object reference.</summary>
public class PCGDataPtrWrapperPropertyData : PropertyData<FPackageIndex>
{
    public PCGDataPtrWrapperPropertyData(FName name) : base(name) { }
    public PCGDataPtrWrapperPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("PCGDataPtrWrapper");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        Value = new FPackageIndex(reader);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        return Value.Write(writer);
    }
}

/// <summary>One FPCGPointArrayProperty: the value count, the single value used while unallocated, and the per-point values.</summary>
public class FPCGPointArrayPropertyValues
{
    [JsonProperty] public int NumValues;
    [JsonProperty] public object[] Value;
    [JsonProperty] public List<object[]> Values;
}

/// <summary>
/// PCG PCGPointArray.cpp FPCGPointArray::Serialize: the point count, then transform, density, bounds min and max, color,
/// steepness, seed and metadata entry, each an FPCGPointArrayProperty of FTransform (quaternion, translation, scale),
/// float, FVector, FVector, FVector4, float, int32 and int64.
/// </summary>
public class PCGPointArrayPropertyData : PropertyData
{
    enum Element { Double, Float, Int32, Int64 }
    static readonly (string Name, Element Type, int Count)[] Properties =
    [
        ("Transform", Element.Double, 10), ("Density", Element.Float, 1), ("BoundsMin", Element.Double, 3), ("BoundsMax", Element.Double, 3),
        ("Color", Element.Double, 4), ("Steepness", Element.Float, 1), ("Seed", Element.Int32, 1), ("MetadataEntry", Element.Int64, 1),
    ];

    [JsonProperty] public int NumPoints;
    [JsonProperty] public Dictionary<string, FPCGPointArrayPropertyValues> PointProperties;

    public PCGPointArrayPropertyData(FName name) : base(name) { }
    public PCGPointArrayPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("PCGPointArray");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    static int Size(Element e) => e is Element.Double or Element.Int64 ? 8 : 4;

    static object[] ReadValue(AssetBinaryReader r, Element e, int count)
    {
        var v = new object[count];
        for (int i = 0; i < count; i++)
            v[i] = e switch { Element.Double => r.ReadDouble(), Element.Float => r.ReadSingle(), Element.Int32 => r.ReadInt32(), _ => (object)r.ReadInt64() };
        return v;
    }

    static void WriteValue(AssetBinaryWriter w, Element e, object[] v)
    {
        foreach (var x in v)
            switch (e)
            {
                case Element.Double: w.Write(Convert.ToDouble(x)); break;
                case Element.Float: w.Write(Convert.ToSingle(x)); break;
                case Element.Int32: w.Write(Convert.ToInt32(x)); break;
                default: w.Write(Convert.ToInt64(x)); break;
            }
    }

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        NumPoints = reader.ReadInt32();
        PointProperties = new Dictionary<string, FPCGPointArrayPropertyValues>();
        foreach (var (name, type, count) in Properties)
        {
            var p = new FPCGPointArrayPropertyValues { NumValues = reader.ReadInt32(), Value = ReadValue(reader, type, count) };
            int n = reader.ReadInt32();
            if (n < 0 || (long)n * Size(type) * count > reader.BaseStream.Length - reader.BaseStream.Position) throw new FormatException($"Invalid PCG point {name} count {n}");
            p.Values = new List<object[]>(n);
            for (int i = 0; i < n; i++) p.Values.Add(ReadValue(reader, type, count));
            PointProperties[name] = p;
        }
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        writer.Write(NumPoints);
        foreach (var (name, type, _) in Properties)
        {
            var p = PointProperties[name];
            writer.Write(p.NumValues);
            WriteValue(writer, type, p.Value);
            writer.Write(p.Values.Count);
            foreach (var v in p.Values) WriteValue(writer, type, v);
        }
        return (int)(writer.BaseStream.Position - start);
    }
}
