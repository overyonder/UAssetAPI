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

/// <summary>
/// UAF BindableValue/UAFBindableTypes.cpp FBindableValueBase::Serialize, which DECLARE_BINDABLE_STRUCT_OPS gives each
/// FBindable type: whether a binding follows and its tagged FUAFPropertyBinding; it then returns false, so the struct's own
/// tagged properties follow.
/// </summary>
public abstract class BindableValuePropertyData : NativeFieldsPropertyData
{
    protected BindableValuePropertyData(FName name) : base(name) { }
    protected BindableValuePropertyData() { }
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        bool bound = Bool(r);
        f.Add(bound ? Tagged(r, "UAFPropertyBinding") : null);
        f.Add(Tagged(r, PropertyType.Value));
    }
}
public class BindableBoolPropertyData : BindableValuePropertyData { public BindableBoolPropertyData(FName n) : base(n) { } public BindableBoolPropertyData() { } static readonly FString T = new("BindableBool"); public override FString PropertyType => T; }
public class BindableBytePropertyData : BindableValuePropertyData { public BindableBytePropertyData(FName n) : base(n) { } public BindableBytePropertyData() { } static readonly FString T = new("BindableByte"); public override FString PropertyType => T; }
public class BindableDoublePropertyData : BindableValuePropertyData { public BindableDoublePropertyData(FName n) : base(n) { } public BindableDoublePropertyData() { } static readonly FString T = new("BindableDouble"); public override FString PropertyType => T; }
public class BindableEnumPropertyData : BindableValuePropertyData { public BindableEnumPropertyData(FName n) : base(n) { } public BindableEnumPropertyData() { } static readonly FString T = new("BindableEnum"); public override FString PropertyType => T; }
public class BindableFloatPropertyData : BindableValuePropertyData { public BindableFloatPropertyData(FName n) : base(n) { } public BindableFloatPropertyData() { } static readonly FString T = new("BindableFloat"); public override FString PropertyType => T; }
public class BindableInt32PropertyData : BindableValuePropertyData { public BindableInt32PropertyData(FName n) : base(n) { } public BindableInt32PropertyData() { } static readonly FString T = new("BindableInt32"); public override FString PropertyType => T; }
public class BindableInt64PropertyData : BindableValuePropertyData { public BindableInt64PropertyData(FName n) : base(n) { } public BindableInt64PropertyData() { } static readonly FString T = new("BindableInt64"); public override FString PropertyType => T; }
public class BindableNamePropertyData : BindableValuePropertyData { public BindableNamePropertyData(FName n) : base(n) { } public BindableNamePropertyData() { } static readonly FString T = new("BindableName"); public override FString PropertyType => T; }
public class BindableObjectPropertyData : BindableValuePropertyData { public BindableObjectPropertyData(FName n) : base(n) { } public BindableObjectPropertyData() { } static readonly FString T = new("BindableObject"); public override FString PropertyType => T; }
public class BindableQuatPropertyData : BindableValuePropertyData { public BindableQuatPropertyData(FName n) : base(n) { } public BindableQuatPropertyData() { } static readonly FString T = new("BindableQuat"); public override FString PropertyType => T; }
public class BindableStructPropertyData : BindableValuePropertyData { public BindableStructPropertyData(FName n) : base(n) { } public BindableStructPropertyData() { } static readonly FString T = new("BindableStruct"); public override FString PropertyType => T; }
public class BindableTransformPropertyData : BindableValuePropertyData { public BindableTransformPropertyData(FName n) : base(n) { } public BindableTransformPropertyData() { } static readonly FString T = new("BindableTransform"); public override FString PropertyType => T; }
public class BindableVectorPropertyData : BindableValuePropertyData { public BindableVectorPropertyData(FName n) : base(n) { } public BindableVectorPropertyData() { } static readonly FString T = new("BindableVector"); public override FString PropertyType => T; }

/// <summary>Structs whose native serializer writes their own tagged properties, under a custom version or not.</summary>
public abstract class SelfTaggedPropertyData : NativeFieldsPropertyData
{
    protected SelfTaggedPropertyData(FName name) : base(name) { }
    protected SelfTaggedPropertyData() { }
    protected override void ReadFields(AssetBinaryReader r, List<object> f) => f.Add(Tagged(r, PropertyType.Value));
}
/// <summary>RemoteControl RemoteControlField.cpp FRemoteControlProperty::Serialize.</summary>
public class RemoteControlPropertyPropertyData : SelfTaggedPropertyData { public RemoteControlPropertyPropertyData(FName n) : base(n) { } public RemoteControlPropertyPropertyData() { } static readonly FString T = new("RemoteControlProperty"); public override FString PropertyType => T; }
/// <summary>AvalancheSequence AvaSequenceShared.cpp FAvaSequenceTime::Serialize.</summary>
public class AvaSequenceTimePropertyData : SelfTaggedPropertyData { public AvaSequenceTimePropertyData(FName n) : base(n) { } public AvaSequenceTimePropertyData() { } static readonly FString T = new("AvaSequenceTime"); public override FString PropertyType => T; }

/// <summary>
/// AvalancheMedia AvaPlayableRemoteControlValues.cpp FAvaPlayableRemoteControlValue::Serialize: from ValueAsString its own
/// tagged properties, before it those of FAvaPlayableRemoteControlValueAsBytes_Legacy.
/// </summary>
public class AvaPlayableRemoteControlValuePropertyData : NativeFieldsPropertyData
{
    static readonly Guid VersionGuid = UAPUtils.GUID(0x85218F83, 0xEDF141CA, 0x800EF947, 0x2F14CB06);
    public AvaPlayableRemoteControlValuePropertyData(FName name) : base(name) { }
    public AvaPlayableRemoteControlValuePropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("AvaPlayableRemoteControlValue");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) =>
        f.Add(Tagged(r, r.Asset.GetCustomVersion(VersionGuid) >= 1 ? "AvaPlayableRemoteControlValue" : "AvaPlayableRemoteControlValueAsBytes_Legacy"));
}

/// <summary>
/// RemoteControl RemoteControlProtocolBinding.cpp operator&lt;&lt;(FRemoteControlProtocolBinding): its tagged properties, then
/// the protocol entity, a TStructOnScope: the struct's path and, when there is one, that struct's tagged properties.
/// </summary>
public class RemoteControlProtocolBindingPropertyData : NativeFieldsPropertyData
{
    public RemoteControlProtocolBindingPropertyData(FName name) : base(name) { }
    public RemoteControlProtocolBindingPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("RemoteControlProtocolBinding");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        f.Add(Tagged(r, "RemoteControlProtocolBinding"));
        string path = r.ReadFString()?.Value;
        f.Add(path);
        if (!string.IsNullOrEmpty(path)) f.Add(Tagged(r, StructName(path)));
    }
    internal static string StructName(string path) => path.Substring(path.LastIndexOfAny(['.', ':']) + 1);
}

/// <summary>
/// RemoteControl RemoteControlExposeRegistry.cpp FRCEntityWrapper::Serialize: the entity struct's path, then that struct's
/// tagged properties.
/// </summary>
public class RCEntityWrapperPropertyData : NativeFieldsPropertyData
{
    public RCEntityWrapperPropertyData(FName name) : base(name) { }
    public RCEntityWrapperPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("RCEntityWrapper");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        var path = new FSoftObjectPath(r);
        f.Add(path);
        string name = path.AssetPath.AssetName?.Value?.Value;
        if (string.IsNullOrEmpty(name)) throw new FormatException("Remote control entity wrapper without an entity type");
        f.Add(Tagged(r, name));
    }
}

/// <summary>
/// PCG PCGPoint.cpp FPCGPoint::Serialize, from FFortniteReleaseBranchCustomObjectVersion::PCGPointStructuredSerializer: a
/// mask of the fields that differ from the defaults, the transform, then each masked field (density, bounds minimum and
/// maximum, colour, steepness, seed, metadata entry). Before it the point is tagged.
/// </summary>
public class PCGPointPropertyData : NativeFieldsPropertyData
{
    static readonly Guid FortniteReleaseGuid = UAPUtils.GUID(0xE7086368, 0x6B234C58, 0x84391B70, 0x16265E91);
    public PCGPointPropertyData(FName name) : base(name) { }
    public PCGPointPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("PCGPoint");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        if (r.Asset.GetCustomVersion(FortniteReleaseGuid) < 13) { f.Add(Tagged(r, "PCGPoint")); return; }
        var c = new ClassSerializers.Cursor(r);
        byte mask = r.ReadByte();
        f.Add(mask);
        f.Add(c.Transform());
        if ((mask & 1) != 0) f.Add(r.ReadSingle());
        if ((mask & 2) != 0) f.Add(c.Vector());
        if ((mask & 4) != 0) f.Add(c.Vector());
        if ((mask & 8) != 0) f.Add(c.Vector4());
        if ((mask & 16) != 0) f.Add(r.ReadSingle());
        if ((mask & 32) != 0) f.Add(r.ReadInt32());
        if ((mask & 64) != 0) f.Add(r.ReadInt64());
    }
}

/// <summary>
/// Engine WorldPartition/Filter/WorldPartitionActorFilter.cpp FWorldPartitionActorFilter::Serialize: the data layer
/// filters (asset path, as a string outside one range of versions, and whether it is included), then the child filters
/// by actor GUID, each a filter itself.
/// </summary>
public class WorldPartitionActorFilterPropertyData : NativeFieldsPropertyData
{
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    public WorldPartitionActorFilterPropertyData(FName name) : base(name) { }
    public WorldPartitionActorFilterPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("WorldPartitionActorFilter");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) => f.Add(ReadFilter(r, 0));
    static object ReadFilter(AssetBinaryReader r, int depth)
    {
        if (depth > 64) throw new FormatException("World partition actor filter nested too deeply");
        var c = new ClassSerializers.Cursor(r);
        int fn = r.Asset.GetCustomVersion(FortniteMainGuid);
        bool asString = fn < 99 || fn >= 102;
        var layers = c.Array<object>(() => new object[] { asString ? c.Str() : new FSoftObjectPath(r), c.Bool() }, 8);
        var children = c.Array<object>(() => new object[] { r.ReadGuid(), ReadFilter(r, depth + 1) }, 24);
        return new object[] { layers, children };
    }
}

/// <summary>Dataflow DataflowImage.cpp FDataflowImage::Serialize: size, slices, format and gamma, then the pixels.</summary>
public class DataflowImagePropertyData : NativeFieldsPropertyData
{
    public DataflowImagePropertyData(FName name) : base(name) { }
    public DataflowImagePropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("DataflowImage");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        f.Add(r.ReadInt32()); f.Add(r.ReadInt32()); f.Add(r.ReadInt32()); f.Add(r.ReadByte()); f.Add(r.ReadByte());
        f.Add(new ClassSerializers.Cursor(r).Bytes64());
    }
}

/// <summary>
/// DataflowNodes DataflowCollectionEditSkinWeightsNode.cpp FDataflowVertexSkinWeightData::Serialize: the count, then the
/// bone indices (int16) and weights (float), each bulk serialized.
/// </summary>
public class DataflowVertexSkinWeightDataPropertyData : NativeFieldsPropertyData
{
    public DataflowVertexSkinWeightDataPropertyData(FName name) : base(name) { }
    public DataflowVertexSkinWeightDataPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("DataflowVertexSkinWeightData");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        var c = new ClassSerializers.Cursor(r);
        f.Add(r.ReadInt32()); f.Add(c.Bulk()); f.Add(c.Bulk());
    }
}

/// <summary>Engine Curves/SimpleCurve.cpp FSimpleCurveKey::Serialize: time and value.</summary>
public class SimpleCurveKeyPropertyData : NativeFieldsPropertyData
{
    public SimpleCurveKeyPropertyData(FName name) : base(name) { }
    public SimpleCurveKeyPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("SimpleCurveKey");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f) { f.Add(r.ReadSingle()); f.Add(r.ReadSingle()); }
}

/// <summary>
/// Chaos GeometryCollection/ManagedArrayCollection.cpp FManagedArrayCollection::Serialize: tagged before
/// FFortniteMainBranchObjectVersion::AddManagedArrayCollectionPropertySerialization, after it the collection's groups and
/// attributes (geometry), kept as the bytes the property tag sizes.
/// </summary>
public class ManagedArrayCollectionPropertyData : NativeFieldsPropertyData
{
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    long size;
    public ManagedArrayCollectionPropertyData(FName name) : base(name) { }
    public ManagedArrayCollectionPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("ManagedArrayCollection");
    public override FString PropertyType => CurrentPropertyType;
    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        size = leng1;
        base.Read(reader, includeHeader, leng1, leng2, serializationContext);
    }
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        if (r.Asset.GetCustomVersion(FortniteMainGuid) < 175) { f.Add(Tagged(r, "ManagedArrayCollection")); return; }
        if (size <= 0 || size > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Managed array collection without a usable property size ({size})");
        f.Add(r.ReadBytes((int)size));
    }
}

/// <summary>
/// ControlRig ControlRigOverride.cpp FControlRigOverrideContainer::Serialize: whether values are keyed by subject, then the
/// values. Each FControlRigOverrideValue::Serialize: the optional subject key; from OverridesStorePathAndLeafPropertyOnly
/// the owner struct's path and the optional property path, before it the property chain; then the offset to the end of
/// the value data, the leaf property hash (and, from OverridesStoreTOCDataForProperties to
/// OverridesStoreLeafPropertyHashOnly, the property size before it), and the leaf property's value, which is typed by
/// the owner struct's property at that path and kept as its bytes.
/// </summary>
public class ControlRigOverrideContainerPropertyData : NativeFieldsPropertyData
{
    static readonly Guid ControlRigGuid = UAPUtils.GUID(0xA7820CFB, 0x20A74359, 0x8C542C14, 0x9623CF50);
    public ControlRigOverrideContainerPropertyData(FName name) : base(name) { }
    public ControlRigOverrideContainerPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("ControlRigOverrideContainer");
    public override FString PropertyType => CurrentPropertyType;
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        var c = new ClassSerializers.Cursor(r);
        int ver = r.Asset.GetCustomVersion(ControlRigGuid);
        f.Add(c.Bool());
        f.Add(c.Array<object>(() =>
        {
            var v = new List<object> { c.Optional(c.Name) };
            if (ver >= 42) { v.Add(new FSoftObjectPath(r)); v.Add(c.Optional(c.Str)); }
            else v.Add(c.Array<object>(() => new object[] { new FFieldPath(r), r.ReadInt32() }, 8));
            long before = r.BaseStream.Position;
            long offset = ver >= 41 ? r.ReadInt64() : r.ReadInt32();
            if (offset <= 0 || before + offset > r.BaseStream.Length) throw new FormatException($"Invalid override data offset {offset}");
            v.Add(r.ReadBytes((int)(before + offset - r.BaseStream.Position)));
            return v;
        }, 9));
    }
}

/// <summary>
/// HairStrandsCore GroomAssetDataflow.cpp FGroomDataflowSettings::Serialize: its tagged properties, then the rest collection
/// (which its constructor always creates) through a Chaos archive, geometry kept as the bytes up to the end of the property.
/// </summary>
public class GroomDataflowSettingsPropertyData : NativeFieldsPropertyData
{
    long size;
    public GroomDataflowSettingsPropertyData(FName name) : base(name) { }
    public GroomDataflowSettingsPropertyData() { }
    private static readonly FString CurrentPropertyType = new FString("GroomDataflowSettings");
    public override FString PropertyType => CurrentPropertyType;
    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        size = leng1;
        base.Read(reader, includeHeader, leng1, leng2, serializationContext);
    }
    protected override void ReadFields(AssetBinaryReader r, List<object> f)
    {
        long end = r.BaseStream.Position + size;
        if (size <= 0 || end > r.BaseStream.Length) throw new FormatException($"Groom dataflow settings without a usable property size ({size})");
        f.Add(Tagged(r, "GroomDataflowSettings"));
        if (r.BaseStream.Position > end) throw new FormatException("Groom dataflow settings overran their property");
        f.Add(r.ReadBytes((int)(end - r.BaseStream.Position)));
    }
}
