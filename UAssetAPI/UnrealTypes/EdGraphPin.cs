using System;
using System.IO;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.UnrealTypes;

public enum EEdGraphPinDirection : byte { Input, Output, Max }
public enum EPinContainerType : byte { None, Array, Set, Map }

/// <summary>The persistent, native FEdGraphTerminalType used for a map value.</summary>
public class FEdGraphTerminalType : ICloneable
{
    public FName TerminalCategory;
    public FName TerminalSubCategory;
    public FPackageIndex TerminalSubCategoryObject;
    public bool IsConst;
    public bool IsWeakPointer;
    public bool IsUObjectWrapper;

    public object Clone()
    {
        var copy = (FEdGraphTerminalType)MemberwiseClone();
        copy.TerminalCategory = (FName)TerminalCategory?.Clone();
        copy.TerminalSubCategory = (FName)TerminalSubCategory?.Clone();
        copy.TerminalSubCategoryObject = TerminalSubCategoryObject == null ? null : new FPackageIndex(TerminalSubCategoryObject.Index);
        return copy;
    }

    /// <summary>EdGraphNode.cpp operator&lt;&lt;(FArchive&amp;, FEdGraphTerminalType&amp;).</summary>
    internal static FEdGraphTerminalType Read(AssetBinaryReader reader)
    {
        var result = new FEdGraphTerminalType
        {
            TerminalCategory = EdGraphPinSerialization.ReadPinName(reader),
            TerminalSubCategory = EdGraphPinSerialization.ReadPinName(reader),
            TerminalSubCategoryObject = EdGraphPinSerialization.ReadObject(reader),
            IsConst = reader.ReadBooleanInt(),
            IsWeakPointer = reader.ReadBooleanInt()
        };
        if (EdGraphPinSerialization.HasUObjectWrapper(reader.Asset)) result.IsUObjectWrapper = reader.ReadBooleanInt();
        return result;
    }

    internal void Write(AssetBinaryWriter writer)
    {
        EdGraphPinSerialization.WritePinName(writer, TerminalCategory);
        EdGraphPinSerialization.WritePinName(writer, TerminalSubCategory);
        EdGraphPinSerialization.WriteObject(writer, TerminalSubCategoryObject);
        writer.WriteBooleanInt(IsConst);
        writer.WriteBooleanInt(IsWeakPointer);
        if (EdGraphPinSerialization.HasUObjectWrapper(writer.Asset)) writer.WriteBooleanInt(IsUObjectWrapper);
    }
}

/// <summary>Native FEdGraphPinType, shared by node pins and reflected struct properties.</summary>
public class FEdGraphPinType : ICloneable
{
    public FName PinCategory;
    public FName PinSubCategory;
    public FPackageIndex PinSubCategoryObject;
    public EPinContainerType ContainerType;
    public FEdGraphTerminalType PinValueType;
    public bool IsReference;
    public bool IsWeakPointer;
    public FPackageIndex MemberParent;
    public FName MemberName;
    public Guid MemberGuid;
    public bool IsConst;
    public bool IsUObjectWrapper;
    public bool SerializeAsSinglePrecisionFloat;

    public object Clone()
    {
        var copy = (FEdGraphPinType)MemberwiseClone();
        copy.PinCategory = (FName)PinCategory?.Clone();
        copy.PinSubCategory = (FName)PinSubCategory?.Clone();
        copy.PinSubCategoryObject = PinSubCategoryObject == null ? null : new FPackageIndex(PinSubCategoryObject.Index);
        copy.PinValueType = (FEdGraphTerminalType)PinValueType?.Clone();
        copy.MemberParent = MemberParent == null ? null : new FPackageIndex(MemberParent.Index);
        copy.MemberName = (FName)MemberName?.Clone();
        return copy;
    }

    /// <summary>
    /// EdGraphPin.cpp FEdGraphPinType::Serialize, field by field with its version gates. Values are kept as stored: the
    /// load-time fixups (asset to softobject, float and double to real) are left to the reader of the data.
    /// </summary>
    public static FEdGraphPinType Read(AssetBinaryReader reader)
    {
        EdGraphPinSerialization.RequirePinTypeLayout(reader.Asset);
        var asset = reader.Asset;
        var result = new FEdGraphPinType
        {
            PinCategory = EdGraphPinSerialization.ReadPinName(reader),
            PinSubCategory = EdGraphPinSerialization.ReadPinName(reader),
            PinSubCategoryObject = EdGraphPinSerialization.ReadObject(reader)
        };
        if (asset.GetCustomVersion<FFrameworkObjectVersion>() >= FFrameworkObjectVersion.EdGraphPinContainerType)
        {
            result.ContainerType = (EPinContainerType)reader.ReadByte();
            if (result.ContainerType > EPinContainerType.Map) throw new FormatException($"Invalid pin container at byte {reader.BaseStream.Position - 1}");
            if (result.ContainerType == EPinContainerType.Map) result.PinValueType = FEdGraphTerminalType.Read(reader);
        }
        else
        {
            bool isMap = false, isSet = false;
            if (EdGraphPinSerialization.HasAdvancedContainers(asset))
            {
                isMap = reader.ReadBooleanInt();
                if (isMap) result.PinValueType = FEdGraphTerminalType.Read(reader);
                isSet = reader.ReadBooleanInt();
            }
            bool isArray = reader.ReadBooleanInt();
            // FEdGraphPinType::ToPinContainerType
            result.ContainerType = isArray ? EPinContainerType.Array : isSet ? EPinContainerType.Set : isMap ? EPinContainerType.Map : EPinContainerType.None;
        }
        result.IsReference = reader.ReadBooleanInt();
        result.IsWeakPointer = reader.ReadBooleanInt();
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_MEMBERREFERENCE_IN_PINTYPE)
        {
            result.MemberParent = EdGraphPinSerialization.ReadObject(reader);
            result.MemberName = EdGraphPinSerialization.ReadName(reader);
            result.MemberGuid = reader.ReadGuid();
        }
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_PINTYPE_CONST) result.IsConst = reader.ReadBooleanInt();
        if (EdGraphPinSerialization.HasUObjectWrapper(asset)) result.IsUObjectWrapper = reader.ReadBooleanInt();
        if (EdGraphPinSerialization.HasSinglePrecisionFlag(asset)) result.SerializeAsSinglePrecisionFloat = reader.ReadBooleanInt();
        return result;
    }

    public void Write(AssetBinaryWriter writer)
    {
        EdGraphPinSerialization.RequirePinTypeLayout(writer.Asset);
        var asset = writer.Asset;
        if (ContainerType > EPinContainerType.Map || (ContainerType == EPinContainerType.Map) != (PinValueType != null))
            throw new FormatException("Invalid pin container or map terminal type");
        EdGraphPinSerialization.WritePinName(writer, PinCategory);
        EdGraphPinSerialization.WritePinName(writer, PinSubCategory);
        EdGraphPinSerialization.WriteObject(writer, PinSubCategoryObject);
        if (asset.GetCustomVersion<FFrameworkObjectVersion>() >= FFrameworkObjectVersion.EdGraphPinContainerType)
        {
            writer.Write((byte)ContainerType);
            PinValueType?.Write(writer);
        }
        else
        {
            if (EdGraphPinSerialization.HasAdvancedContainers(asset))
            {
                writer.WriteBooleanInt(ContainerType == EPinContainerType.Map);
                PinValueType?.Write(writer);
                writer.WriteBooleanInt(ContainerType == EPinContainerType.Set);
            }
            else if (ContainerType > EPinContainerType.Array) throw new FormatException("Sets and maps need FBlueprintsObjectVersion::AdvancedContainerSupport");
            writer.WriteBooleanInt(ContainerType == EPinContainerType.Array);
        }
        writer.WriteBooleanInt(IsReference);
        writer.WriteBooleanInt(IsWeakPointer);
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_MEMBERREFERENCE_IN_PINTYPE)
        {
            EdGraphPinSerialization.WriteObject(writer, MemberParent);
            writer.Write(MemberName);
            writer.Write(MemberGuid);
        }
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_PINTYPE_CONST) writer.WriteBooleanInt(IsConst);
        if (EdGraphPinSerialization.HasUObjectWrapper(asset)) writer.WriteBooleanInt(IsUObjectWrapper);
        if (EdGraphPinSerialization.HasSinglePrecisionFlag(asset)) writer.WriteBooleanInt(SerializeAsSinglePrecisionFloat);
    }
}

public class FUserPinInfo
{
    public FName PinName;
    public FEdGraphPinType PinType;
    public EEdGraphPinDirection DesiredPinDirection;
    public FString PinDefaultValue;

    /// <summary>K2Node_EditablePinBase.cpp operator&lt;&lt;(FArchive&amp;, FUserPinInfo&amp;). Before
    /// VER_UE4_SERIALIZE_PINTYPE_CONST the pin type was a short legacy record and no direction was stored.</summary>
    internal static FUserPinInfo Read(AssetBinaryReader reader)
    {
        var result = new FUserPinInfo { PinName = EdGraphPinSerialization.ReadPinName(reader) };
        if (reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_PINTYPE_CONST)
        {
            result.PinType = FEdGraphPinType.Read(reader);
            // FUserPinInfo's constructor leaves DesiredPinDirection at EGPD_MAX, and pins created that way save it
            result.DesiredPinDirection = (EEdGraphPinDirection)reader.ReadByte();
            if (result.DesiredPinDirection > EEdGraphPinDirection.Max) throw new FormatException($"Invalid signature pin direction at byte {reader.BaseStream.Position - 1}");
        }
        else
        {
            bool isArray = reader.ReadBooleanInt();
            result.PinType = new FEdGraphPinType
            {
                ContainerType = isArray ? EPinContainerType.Array : EPinContainerType.None,
                IsReference = reader.ReadBooleanInt(),
                PinCategory = FName.DefineDummy(reader.Asset, reader.ReadFString()),
                PinSubCategory = FName.DefineDummy(reader.Asset, reader.ReadFString()),
                PinSubCategoryObject = EdGraphPinSerialization.ReadObject(reader)
            };
            result.DesiredPinDirection = EEdGraphPinDirection.Max;
        }
        result.PinDefaultValue = reader.ReadFString();
        return result;
    }

    internal void Write(AssetBinaryWriter writer)
    {
        EdGraphPinSerialization.WritePinName(writer, PinName);
        if (writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_PINTYPE_CONST)
        {
            if (DesiredPinDirection > EEdGraphPinDirection.Max) throw new FormatException("Invalid signature pin direction");
            PinType.Write(writer);
            writer.Write((byte)DesiredPinDirection);
        }
        else
        {
            writer.WriteBooleanInt(PinType.ContainerType == EPinContainerType.Array);
            writer.WriteBooleanInt(PinType.IsReference);
            writer.Write(PinType.PinCategory.Value);
            writer.Write(PinType.PinSubCategory.Value);
            EdGraphPinSerialization.WriteObject(writer, PinType.PinSubCategoryObject);
        }
        writer.Write(PinDefaultValue);
    }
}

public class EdGraphPinReference
{
    public FPackageIndex OwningNode;
    public Guid PinId;

    internal static EdGraphPinReference Read(AssetBinaryReader reader)
    {
        if (reader.ReadBooleanInt()) return null;
        var result = new EdGraphPinReference { OwningNode = EdGraphPinSerialization.ReadObject(reader), PinId = reader.ReadGuid() };
        if (result.OwningNode.Index == 0 || result.PinId == Guid.Empty) throw new FormatException($"Invalid pin identity at byte {reader.BaseStream.Position - 20}");
        return result;
    }

    internal static void Write(AssetBinaryWriter writer, EdGraphPinReference reference)
    {
        writer.WriteBooleanInt(reference == null);
        if (reference == null) return;
        if (reference.OwningNode == null || reference.OwningNode.Index == 0 || reference.PinId == Guid.Empty) throw new FormatException("Invalid pin identity");
        EdGraphPinSerialization.WriteObject(writer, reference.OwningNode);
        writer.Write(reference.PinId);
    }
}

/// <summary>A persistent UEdGraphPin owned by a node. References are owner/GUID pairs.</summary>
public class EdGraphPin : EdGraphPinReference
{
    public FName PinName;
    public TextPropertyData PinFriendlyName;
    public int SourceIndex;
    public FString PinToolTip;
    public EEdGraphPinDirection Direction;
    public FEdGraphPinType PinType;
    public FString DefaultValue;
    public FString AutogeneratedDefaultValue;
    public FPackageIndex DefaultObject;
    public TextPropertyData DefaultTextValue;
    public EdGraphPinReference[] LinkedTo;
    public EdGraphPinReference[] SubPins;
    public EdGraphPinReference ParentPin;
    public EdGraphPinReference ReferencePassThroughConnection;
    public Guid PersistentGuid;
    public uint Flags;

    internal static new EdGraphPin Read(AssetBinaryReader reader)
    {
        var identity = EdGraphPinReference.Read(reader);
        if (identity == null) return null;
        var pin = new EdGraphPin
        {
            OwningNode = EdGraphPinSerialization.ReadObject(reader),
            PinId = reader.ReadGuid(),
            PinName = EdGraphPinSerialization.ReadPinName(reader),
            PinFriendlyName = ReadText(reader),
            SourceIndex = EdGraphPinSerialization.HasSourceIndex(reader.Asset) ? reader.ReadInt32() : -1,
            PinToolTip = reader.ReadFString(),
            Direction = EdGraphPinSerialization.ReadDirection(reader),
            PinType = FEdGraphPinType.Read(reader),
            DefaultValue = reader.ReadFString(),
            AutogeneratedDefaultValue = reader.ReadFString(),
            DefaultObject = EdGraphPinSerialization.ReadObject(reader),
            DefaultTextValue = ReadText(reader),
            LinkedTo = ReadReferences(reader),
            SubPins = ReadReferences(reader),
            ParentPin = EdGraphPinReference.Read(reader),
            ReferencePassThroughConnection = EdGraphPinReference.Read(reader),
            PersistentGuid = reader.ReadGuid(),
            Flags = reader.ReadUInt32()
        };
        if (!identity.OwningNode.Equals(pin.OwningNode) || identity.PinId != pin.PinId)
            throw new FormatException($"Owning pin identity mismatch at byte {reader.BaseStream.Position}");
        return pin;
    }

    private static TextPropertyData ReadText(AssetBinaryReader reader)
    {
        var text = new TextPropertyData();
        text.Read(reader, false, 0);
        return text;
    }

    private static EdGraphPinReference[] ReadReferences(AssetBinaryReader reader)
    {
        var references = new EdGraphPinReference[EdGraphPinSerialization.ReadCount(reader, 4)];
        for (int i = 0; i < references.Length; i++) references[i] = EdGraphPinReference.Read(reader);
        return references;
    }

    internal static void WriteReferences(AssetBinaryWriter writer, EdGraphPinReference[] references)
    {
        EdGraphPinSerialization.WriteCount(writer, references?.Length);
        foreach (var reference in references) EdGraphPinReference.Write(writer, reference);
    }

    internal void Write(AssetBinaryWriter writer)
    {
        if (Direction > EEdGraphPinDirection.Output) throw new FormatException("Invalid pin direction");
        EdGraphPinReference.Write(writer, this);
        EdGraphPinSerialization.WriteObject(writer, OwningNode);
        writer.Write(PinId);
        EdGraphPinSerialization.WritePinName(writer, PinName);
        PinFriendlyName.Write(writer, false);
        if (EdGraphPinSerialization.HasSourceIndex(writer.Asset)) writer.Write(SourceIndex);
        writer.Write(PinToolTip);
        writer.Write((byte)Direction);
        PinType.Write(writer);
        writer.Write(DefaultValue);
        writer.Write(AutogeneratedDefaultValue);
        EdGraphPinSerialization.WriteObject(writer, DefaultObject);
        DefaultTextValue.Write(writer, false);
        WriteReferences(writer, LinkedTo);
        WriteReferences(writer, SubPins);
        EdGraphPinReference.Write(writer, ParentPin);
        EdGraphPinReference.Write(writer, ReferencePassThroughConnection);
        writer.Write(PersistentGuid);
        writer.Write(Flags);
    }
}

internal static class EdGraphPinSerialization
{
    static readonly Guid UE5MainStreamGuid = UAPUtils.GUID(0x697dd581, 0xe64f41ab, 0xaa4a51ec, 0xbeb7b628);
    static readonly Guid BlueprintsGuid = UAPUtils.GUID(0xb0d832e4, 0x1f894f0d, 0xaccf7eb7, 0x36fd4aa2);

    internal static void RequireProfile(UAsset asset)
    {
        if (!SupportsProfile(asset))
            throw new NotSupportedException("Native editor pins need an uncooked, tagged, unfiltered package with owned pin arrays (FBlueprintsObjectVersion::EdGraphPinOptimized)");
    }

    /// <summary>
    /// EdGraphNode.cpp UEdGraphNode::Serialize stores pins natively since FBlueprintsObjectVersion::EdGraphPinOptimized;
    /// before that they were separate UEdGraphPin_Deprecated exports. The editor-only pin fields need an unfiltered,
    /// uncooked package. Every other field is gated where it's read, as UEdGraphPin::Serialize gates it.
    /// </summary>
    internal static bool SupportsProfile(UAsset asset) =>
        !asset.IsFilterEditorOnly && !asset.HasUnversionedProperties && !asset.PackageFlags.HasFlag(EPackageFlags.PKG_Cooked) &&
        asset.GetCustomVersion(BlueprintsGuid) >= 4 && asset.ObjectVersion >= ObjectVersion.VER_UE4_EDGRAPHPINTYPE_SERIALIZATION;

    /// <summary>FEdGraphPinType::Serialize returns false before VER_UE4_EDGRAPHPINTYPE_SERIALIZATION, leaving the struct tagged.</summary>
    internal static void RequirePinTypeLayout(UAsset asset)
    {
        if (asset.ObjectVersion < ObjectVersion.VER_UE4_EDGRAPHPINTYPE_SERIALIZATION || asset.HasUnversionedProperties)
            throw new NotSupportedException("FEdGraphPinType is natively serialized only in tagged packages since VER_UE4_EDGRAPHPINTYPE_SERIALIZATION");
    }

    /// <summary>FFrameworkObjectVersion::PinsStoreFName: pin, category and parameter names are FNames, before it FStrings.</summary>
    internal static bool PinNamesAreFNames(UAsset asset) => asset.GetCustomVersion<FFrameworkObjectVersion>() >= FFrameworkObjectVersion.PinsStoreFName;

    /// <summary>FBlueprintsObjectVersion::AdvancedContainerSupport (6): set and map flags in the pre-EdGraphPinContainerType layout.</summary>
    internal static bool HasAdvancedContainers(UAsset asset) => asset.GetCustomVersion(BlueprintsGuid) >= 6;

    /// <summary>FReleaseObjectVersion::PinTypeIncludesUObjectWrapperFlag.</summary>
    internal static bool HasUObjectWrapper(UAsset asset) =>
        asset.GetCustomVersion<FReleaseObjectVersion>() >= FReleaseObjectVersion.PinTypeIncludesUObjectWrapperFlag;

    internal static FName ReadPinName(AssetBinaryReader reader) =>
        PinNamesAreFNames(reader.Asset) ? ReadName(reader) : FName.DefineDummy(reader.Asset, reader.ReadFString());

    internal static void WritePinName(AssetBinaryWriter writer, FName name)
    {
        if (PinNamesAreFNames(writer.Asset)) writer.Write(name);
        else writer.Write(name.Value);
    }

    /// <summary>FUE5MainStreamObjectVersion::EdGraphPinSourceIndex (50).</summary>
    internal static bool HasSourceIndex(UAsset asset) => asset.GetCustomVersion(UE5MainStreamGuid) >= 50;

    /// <summary>FUE5ReleaseStreamObjectVersion::SerializeFloatPinDefaultValuesAsSinglePrecision.</summary>
    internal static bool HasSinglePrecisionFlag(UAsset asset) =>
        asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() >= FUE5ReleaseStreamObjectVersion.SerializeFloatPinDefaultValuesAsSinglePrecision;

    internal static int ReadCount(AssetBinaryReader reader, int minimumBytes)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > MainSerializer.MaxSerializedArrayLength || count > (reader.BaseStream.Length - reader.BaseStream.Position) / minimumBytes)
            throw new FormatException($"Invalid pin array count {count} at byte {reader.BaseStream.Position - 4}");
        return count;
    }

    internal static void WriteCount(AssetBinaryWriter writer, int? count)
    {
        if (!count.HasValue || count < 0 || count > MainSerializer.MaxSerializedArrayLength) throw new FormatException("Invalid pin array count");
        writer.Write(count.Value);
    }

    internal static EEdGraphPinDirection ReadDirection(AssetBinaryReader reader)
    {
        var direction = (EEdGraphPinDirection)reader.ReadByte();
        if (direction > EEdGraphPinDirection.Output) throw new FormatException($"Invalid pin direction at byte {reader.BaseStream.Position - 1}");
        return direction;
    }

    internal static FName ReadName(AssetBinaryReader reader)
    {
        int index = reader.ReadInt32(), number = reader.ReadInt32();
        if (index < 0 || index >= reader.Asset.GetNameMapIndexList().Count || number < 0) throw new FormatException($"Invalid pin FName at byte {reader.BaseStream.Position - 8}");
        return new FName(reader.Asset, index, number);
    }

    internal static FPackageIndex ReadObject(AssetBinaryReader reader)
    {
        var index = new FPackageIndex(reader.ReadInt32());
        ValidateObject(reader.Asset, index);
        return index;
    }

    internal static void ValidateObject(UAsset asset, FPackageIndex index)
    {
        if (index == null || index.Index > asset.Exports.Count || (long)index.Index < -(long)asset.Imports.Count)
            throw new FormatException($"Invalid pin package reference {index?.Index}");
    }

    internal static void WriteObject(AssetBinaryWriter writer, FPackageIndex index)
    {
        ValidateObject(writer.Asset, index);
        writer.Write(index.Index);
    }
}
