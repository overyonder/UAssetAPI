using System;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.CustomVersions;
using Newtonsoft.Json;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// Materials/MaterialShared.cpp SerializeExpressionInput and SerializeMaterialInput: the connected expression, output
/// index, input name and component masks, then, for an FMaterialInput, whether the constant is used and the constant.
/// MaterialShared.cpp registers native struct ops (UE_IMPLEMENT_STRUCT) only for the inputs below; FShadingModelMaterialInput
/// and FSubstrateMaterialInput have none, so they are saved as tagged properties.
/// </summary>
public abstract class MaterialInputPropertyData<T> : PropertyData<T>
{
    [JsonProperty] public FPackageIndex Expression;
    [JsonProperty] public int OutputIndex;
    [JsonProperty] public FName InputName;
    [JsonProperty] public FString InputNameOld;
    [JsonProperty] public int Mask;
    [JsonProperty] public int MaskR;
    [JsonProperty] public int MaskG;
    [JsonProperty] public int MaskB;
    [JsonProperty] public int MaskA;
    [JsonProperty] public FName ExpressionName;
    /// <summary>Whether the material input uses its constant instead of the expression; null for an expression input.</summary>
    [JsonProperty] public bool? UseConstant;

    public MaterialInputPropertyData() { }
    public MaterialInputPropertyData(FName name) : base(name) { }

    /// <summary>An FMaterialInput, which has a constant; FExpressionInput and FMaterialAttributesInput have none.</summary>
    protected virtual bool HasConstant => false;
    protected virtual void ReadConstant(AssetBinaryReader reader) { }
    protected virtual void WriteConstant(AssetBinaryWriter writer) { }

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);

        // older inputs are tagged structs, which these readers don't decode
        if (reader.Asset.GetCustomVersion<FCoreObjectVersion>() < FCoreObjectVersion.MaterialInputNativeSerialize)
            throw new FormatException($"{PropertyType} saved before FCoreObjectVersion::MaterialInputNativeSerialize");

        if ((reader.Asset.GetEngineVersion() <= EngineVersion.VER_UE5_1 && !reader.Asset.IsFilterEditorOnly) || reader.Asset.GetEngineVersion() >= EngineVersion.VER_UE5_1)
            Expression = reader.XFERPTR();
        OutputIndex = reader.ReadInt32();
        if (reader.Asset.GetCustomVersion<FFrameworkObjectVersion>() >= FFrameworkObjectVersion.PinsStoreFName) InputName = reader.ReadFName();
        else InputNameOld = reader.ReadFString();
        Mask = reader.ReadInt32();
        MaskR = reader.ReadInt32();
        MaskG = reader.ReadInt32();
        MaskB = reader.ReadInt32();
        MaskA = reader.ReadInt32();
        ExpressionName = reader.Asset.GetEngineVersion() <= EngineVersion.VER_UE5_1 && reader.Asset.IsFilterEditorOnly ? reader.ReadFName() : null;

        if (!HasConstant) return;
        uint use = reader.ReadUInt32();
        if (use > 1) throw new FormatException($"Invalid {PropertyType} UseConstant {use}");
        UseConstant = use == 1;
        ReadConstant(reader);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);

        long start = writer.BaseStream.Position;
        if ((writer.Asset.GetEngineVersion() <= EngineVersion.VER_UE5_1 && !writer.Asset.IsFilterEditorOnly) || writer.Asset.GetEngineVersion() >= EngineVersion.VER_UE5_1)
            writer.XFERPTR(Expression);
        writer.Write(OutputIndex);
        if (writer.Asset.GetCustomVersion<FFrameworkObjectVersion>() >= FFrameworkObjectVersion.PinsStoreFName) writer.Write(InputName);
        else writer.Write(InputNameOld);
        writer.Write(Mask);
        writer.Write(MaskR);
        writer.Write(MaskG);
        writer.Write(MaskB);
        writer.Write(MaskA);
        if (writer.Asset.GetEngineVersion() <= EngineVersion.VER_UE5_1 && writer.Asset.IsFilterEditorOnly) writer.Write(ExpressionName);

        if (HasConstant)
        {
            writer.Write(UseConstant == true ? 1u : 0u);
            WriteConstant(writer);
        }
        return (int)(writer.BaseStream.Position - start);
    }

    protected override void HandleCloned(PropertyData res)
    {
        MaterialInputPropertyData<T> cloningProperty = (MaterialInputPropertyData<T>)res;
        cloningProperty.InputName = (FName)this.InputName?.Clone();
        cloningProperty.InputNameOld = (FString)this.InputNameOld?.Clone();
        cloningProperty.ExpressionName = (FName)this.ExpressionName?.Clone();
    }
}

/// <summary>A material input whose constant is a struct.</summary>
public abstract class StructMaterialInputPropertyData<T> : MaterialInputPropertyData<T> where T : PropertyData
{
    public StructMaterialInputPropertyData() { }
    public StructMaterialInputPropertyData(FName name) : base(name) { }

    protected override bool HasConstant => true;
    protected abstract T NewConstant(AssetBinaryReader reader);

    protected override void ReadConstant(AssetBinaryReader reader)
    {
        Value = NewConstant(reader);
        Value.Ancestry.Initialize(Ancestry, Name);
        Value.Read(reader, false, 0);
    }

    protected override void WriteConstant(AssetBinaryWriter writer) => Value.Write(writer, false);

    public override void ResolveAncestries(UAsset asset, AncestryInfo ancestrySoFar)
    {
        var ancestryNew = (AncestryInfo)ancestrySoFar.Clone();
        ancestryNew.SetAsParent(Name);
        Value?.ResolveAncestries(asset, ancestryNew);
        base.ResolveAncestries(asset, ancestrySoFar);
    }
}

public class ExpressionInputPropertyData : MaterialInputPropertyData<int>
{
    public ExpressionInputPropertyData(FName name) : base(name) { }
    public ExpressionInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("ExpressionInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}

public class MaterialAttributesInputPropertyData : MaterialInputPropertyData<int>
{
    public MaterialAttributesInputPropertyData(FName name) : base(name) { }
    public MaterialAttributesInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("MaterialAttributesInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}

/// <summary>FColorMaterialInput: an FColor constant before FFortniteMainBranchObjectVersion::MaterialInputUsesLinearColor, then an FLinearColor.</summary>
public class ColorMaterialInputPropertyData : StructMaterialInputPropertyData<PropertyData>
{
    public ColorMaterialInputPropertyData(FName name) : base(name) { }
    public ColorMaterialInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("ColorMaterialInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    protected override PropertyData NewConstant(AssetBinaryReader reader) =>
        reader.Asset.GetCustomVersion<FFortniteMainBranchObjectVersion>() < FFortniteMainBranchObjectVersion.MaterialInputUsesLinearColor
            ? new ColorPropertyData(Name) : new LinearColorPropertyData(Name);
}

public class ScalarMaterialInputPropertyData : MaterialInputPropertyData<float>
{
    public ScalarMaterialInputPropertyData(FName name) : base(name) { }
    public ScalarMaterialInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("ScalarMaterialInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    protected override bool HasConstant => true;
    protected override void ReadConstant(AssetBinaryReader reader) => Value = reader.ReadSingle();
    protected override void WriteConstant(AssetBinaryWriter writer) => writer.Write(Value);
}

/// <summary>FVectorMaterialInput: an FVector3f constant.</summary>
public class VectorMaterialInputPropertyData : StructMaterialInputPropertyData<Vector3fPropertyData>
{
    public VectorMaterialInputPropertyData(FName name) : base(name) { }
    public VectorMaterialInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("VectorMaterialInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    protected override Vector3fPropertyData NewConstant(AssetBinaryReader reader) => new(Name);
}

/// <summary>FVector2MaterialInput: an FVector2f constant.</summary>
public class Vector2MaterialInputPropertyData : StructMaterialInputPropertyData<Vector2fPropertyData>
{
    public Vector2MaterialInputPropertyData(FName name) : base(name) { }
    public Vector2MaterialInputPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector2MaterialInput");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    protected override Vector2fPropertyData NewConstant(AssetBinaryReader reader) => new(Name);
}
