using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// A struct whose native serializer writes fixed fields: they are read into <see cref="Fields"/> and their bytes kept for
/// writing, so a reader only has to know the layout.
/// </summary>
public abstract class NativeFieldsPropertyData : PropertyData
{
    [JsonProperty] public List<object> Fields;
    [JsonProperty] public byte[] Bytes;

    protected NativeFieldsPropertyData(FName name) : base(name) { }
    protected NativeFieldsPropertyData() { }

    public override bool HasCustomStructSerialization => true;

    protected abstract void ReadFields(AssetBinaryReader reader, List<object> fields);

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        long start = reader.BaseStream.Position;
        Fields = new List<object>();
        ReadFields(reader, Fields);
        long end = reader.BaseStream.Position;
        reader.BaseStream.Position = start;
        Bytes = reader.ReadBytes((int)(end - start));
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        writer.Write(Bytes);
        return Bytes.Length;
    }

    protected static StructPropertyData Tagged(AssetBinaryReader reader, string structType)
    {
        var s = new StructPropertyData(FName.DefineDummy(reader.Asset, structType), FName.DefineDummy(reader.Asset, structType));
        s.Read(reader, false, 1, 0, PropertySerializationContext.StructFallback);
        return s;
    }

    protected static bool Bool(AssetBinaryReader reader)
    {
        uint v = reader.ReadUInt32();
        if (v > 1) throw new FormatException($"Invalid bool {v}");
        return v == 1;
    }
}

/// <summary>Misc/FrameRate.cpp FFrameRate::Serialize: numerator, denominator.</summary>
public class FrameRatePropertyData : NativeFieldsPropertyData
{
    public FrameRatePropertyData(FName name) : base(name) { }
    public FrameRatePropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("FrameRate");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) { f.Add(r.ReadInt32()); f.Add(r.ReadInt32()); }
}

/// <summary>FRotator3f, a core variant: pitch, yaw, roll as floats.</summary>
public class Rotator3fPropertyData : NativeFieldsPropertyData
{
    public Rotator3fPropertyData(FName name) : base(name) { }
    public Rotator3fPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("Rotator3f");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) { for (int i = 0; i < 3; i++) f.Add(r.ReadSingle()); }
}

/// <summary>FVector4d, a core variant: X, Y, Z, W as doubles.</summary>
public class Vector4dPropertyData : NativeFieldsPropertyData
{
    public Vector4dPropertyData(FName name) : base(name) { }
    public Vector4dPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("Vector4d");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) { for (int i = 0; i < 4; i++) f.Add(r.ReadDouble()); }
}

/// <summary>FIntVector4, immutable: X, Y, Z, W as int32.</summary>
public class IntVector4PropertyData : NativeFieldsPropertyData
{
    public IntVector4PropertyData(FName name) : base(name) { }
    public IntVector4PropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("IntVector4");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) { for (int i = 0; i < 4; i++) f.Add(r.ReadInt32()); }
}

/// <summary>UAFAnimGraph TraitCore/TraitHandle.cpp FAnimNextTraitHandle::Serialize: the packed trait index and node handle.</summary>
public class AnimNextTraitHandlePropertyData : NativeFieldsPropertyData
{
    public AnimNextTraitHandlePropertyData(FName name) : base(name) { }
    public AnimNextTraitHandlePropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("AnimNextTraitHandle");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) => f.Add(r.ReadUInt32());
}

/// <summary>
/// StateTree StateTreeInstanceData.cpp FStateTreeInstanceData::Serialize: before AddedCustomSerialization the struct's own
/// tagged properties, after it the tagged properties of its FStateTreeInstanceStorage.
/// </summary>
public class StateTreeInstanceDataPropertyData : NativeFieldsPropertyData
{
    static readonly Guid StorageGuid = UAPUtils.GUID(0x60C4F0DE, 0x8B264C34, 0xAA937201, 0x5DFF09CC);
    public StateTreeInstanceDataPropertyData(FName name) : base(name) { }
    public StateTreeInstanceDataPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("StateTreeInstanceData");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) =>
        f.Add(Tagged(r, r.Asset.GetCustomVersion(StorageGuid) < 1 ? "StateTreeInstanceData" : "StateTreeInstanceStorage"));
}

/// <summary>
/// WorldConditions WorldConditionQuery.cpp FWorldConditionQueryDefinition::Serialize: the tagged properties, then from
/// StructSharedDefinition whether a shared definition follows and its tagged properties.
/// </summary>
public class WorldConditionQueryDefinitionPropertyData : NativeFieldsPropertyData
{
    static readonly Guid WorldConditionGuid = UAPUtils.GUID(0x2C28AC22, 0x15CF46FE, 0xBD19F011, 0x652A3C05);
    public WorldConditionQueryDefinitionPropertyData(FName name) : base(name) { }
    public WorldConditionQueryDefinitionPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("WorldConditionQueryDefinition");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        f.Add(Tagged(r, "WorldConditionQueryDefinition"));
        if (r.Asset.GetCustomVersion(WorldConditionGuid) < 1) return;
        bool shared = Bool(r);
        f.Add(shared);
        if (shared) f.Add(Tagged(r, "WorldConditionQuerySharedDefinition"));
    }
}

/// <summary>
/// RigVM RigVMCore/RigVMMemoryStorageStruct.cpp FRigVMMemoryStorageStruct::Serialize: its FInstancedPropertyBag, the memory
/// type and the property path descriptions (property index, head C++ type, segment path).
/// </summary>
public class RigVMMemoryStorageStructPropertyData : NativeFieldsPropertyData
{
    public RigVMMemoryStorageStructPropertyData(FName name) : base(name) { }
    public RigVMMemoryStorageStructPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("RigVMMemoryStorageStruct");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        var bag = new InstancedPropertyBagPropertyData(FName.DefineDummy(r.Asset, "MemoryStorage"));
        bag.Read(r, false, 0);
        f.Add(bag);
        f.Add(r.ReadByte());
        int n = r.ReadInt32();
        if (n < 0 || (long)n * 12 > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Invalid property path count {n}");
        for (int i = 0; i < n; i++) f.Add(new object[] { r.ReadInt32(), r.ReadFString()?.Value, r.ReadFString()?.Value });
    }
}

/// <summary>
/// CurveExpression AnimNode_RemapCurvesBase.cpp SerializeNode: the node's tagged properties, then from SerializedExpressions
/// the cached expressions (TMap&lt;FName, FExpressionObject&gt;) and the expression map hash (TOptional&lt;int32&gt;).
/// </summary>
public class AnimNodeRemapCurvesPropertyData : NativeFieldsPropertyData
{
    static readonly Guid CurveExpressionGuid = UAPUtils.GUID(0xA26D36AE, 0x26935388, 0xA8C5CB96, 0x2B95B4AF);
    public AnimNodeRemapCurvesPropertyData(FName name) : base(name) { }
    public AnimNodeRemapCurvesPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("AnimNode_RemapCurves");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        f.Add(Tagged(r, "AnimNode_RemapCurves"));
        if (r.Asset.GetCustomVersion(CurveExpressionGuid) < 1) return;
        var c = new ClassSerializers.Cursor(r);
        f.Add(c.Array(() => new object[] { c.Name(), c.Expression() }, 12));
        bool hasHash = Bool(r);
        f.Add(hasHash ? r.ReadInt32() : null);
    }
}
