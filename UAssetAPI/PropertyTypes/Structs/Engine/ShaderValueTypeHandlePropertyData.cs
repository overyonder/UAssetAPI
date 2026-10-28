using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

public enum EShaderFundamentalType : byte { Bool = 0, Int = 1, Uint = 2, Float = 3, Struct = 4, None = 255 }
public enum EShaderFundamentalDimensionType : byte { Scalar, Vector, Matrix }

/// <summary>
/// ComputeFramework ShaderParamTypeDefinition.cpp operator&lt;&lt;(FArchive&amp;, FShaderValueTypeHandle&amp;): a shader value type,
/// either a struct of named element types or a scalar, vector or matrix of a fundamental type.
/// </summary>
public class FShaderValueType
{
    static readonly Guid ComputeFrameworkGuid = UAPUtils.GUID(0x6304a3e7, 0x00594f59, 0x8cfc21bd, 0x7721fd4e);

    public EShaderFundamentalType Type;
    /// <summary>Stored from FComputeFrameworkObjectVersion::InitialVersion.</summary>
    public bool IsDynamicArray;
    public FName Name;
    public List<KeyValuePair<FName, FShaderValueType>> StructElements;
    public EShaderFundamentalDimensionType DimensionType;
    public byte VectorElemCount;
    public byte MatrixRowCount;
    public byte MatrixColumnCount;

    static bool HasDynamicArrayFlag(UAsset asset) => asset.GetCustomVersion(ComputeFrameworkGuid) >= 0;

    public static FShaderValueType Read(AssetBinaryReader reader, int depth = 0)
    {
        if (depth > 64)
        {
            reader.Asset.Coverage.Report("pruned-depth", "Shader value type decoding stopped at depth 65 (limit 64).");
            throw new FormatException("Shader value type nesting too deep");
        }
        var result = new FShaderValueType { Type = (EShaderFundamentalType)reader.ReadByte() };
        if (HasDynamicArrayFlag(reader.Asset)) result.IsDynamicArray = reader.ReadBooleanInt();
        if (result.Type == EShaderFundamentalType.Struct)
        {
            result.Name = reader.ReadFName();
            int n = reader.ReadInt32();
            if (n < 0 || n > (reader.BaseStream.Length - reader.BaseStream.Position)) throw new FormatException($"Invalid shader struct element count {n}");
            result.StructElements = new List<KeyValuePair<FName, FShaderValueType>>(n);
            for (int i = 0; i < n; i++) result.StructElements.Add(new(reader.ReadFName(), Read(reader, depth + 1)));
        }
        else
        {
            result.DimensionType = (EShaderFundamentalDimensionType)reader.ReadByte();
            if (result.DimensionType > EShaderFundamentalDimensionType.Matrix) throw new FormatException($"Invalid shader dimension type at byte {reader.BaseStream.Position - 1}");
            if (result.DimensionType == EShaderFundamentalDimensionType.Vector) result.VectorElemCount = reader.ReadByte();
            else if (result.DimensionType == EShaderFundamentalDimensionType.Matrix) { result.MatrixRowCount = reader.ReadByte(); result.MatrixColumnCount = reader.ReadByte(); }
        }
        return result;
    }

    public void Write(AssetBinaryWriter writer)
    {
        writer.Write((byte)Type);
        if (HasDynamicArrayFlag(writer.Asset)) writer.WriteBooleanInt(IsDynamicArray);
        if (Type == EShaderFundamentalType.Struct)
        {
            writer.Write(Name);
            writer.Write(StructElements.Count);
            foreach (var kv in StructElements) { writer.Write(kv.Key); kv.Value.Write(writer); }
        }
        else
        {
            writer.Write((byte)DimensionType);
            if (DimensionType == EShaderFundamentalDimensionType.Vector) writer.Write(VectorElemCount);
            else if (DimensionType == EShaderFundamentalDimensionType.Matrix) { writer.Write(MatrixRowCount); writer.Write(MatrixColumnCount); }
        }
    }
}

/// <summary>FShaderValueTypeHandle, which serializes natively (FShaderValueTypeHandle::Serialize).</summary>
public class ShaderValueTypeHandlePropertyData : PropertyData<FShaderValueType>
{
    public ShaderValueTypeHandlePropertyData() { }
    public ShaderValueTypeHandlePropertyData(FName name) : base(name) { }
    private static readonly FString CurrentPropertyType = new("ShaderValueTypeHandle");
    public override FString PropertyType => CurrentPropertyType;
    public override bool HasCustomStructSerialization => true;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) ReadEndPropertyTag(reader);
        Value = FShaderValueType.Read(reader);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        Value.Write(writer);
        return checked((int)(writer.BaseStream.Position - start));
    }
}
