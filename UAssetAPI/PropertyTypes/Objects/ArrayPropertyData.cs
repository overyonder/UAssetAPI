using Newtonsoft.Json;
using System;
using System.IO;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.PropertyTypes.Objects;

/// <summary>
/// Describes an array.
/// </summary>
public class ArrayPropertyData : PropertyData<PropertyData[]>
{
    [JsonProperty]
    public FName ArrayType;
    [JsonProperty]
    public StructPropertyData DummyStruct;
    /// <summary>
    /// The extension bytes of a struct array's inner FPropertyTag (PropertyArray.cpp writes the whole tag between
    /// VER_UE4_INNER_ARRAY_TAG_INFO and PROPERTY_TAG_COMPLETE_TYPE_NAME), kept as stored.
    /// </summary>
    [JsonProperty]
    public byte[] InnerTagExtensions;

    /// <summary>PropertyTag.cpp SerializePropertyExtensions: a flags byte, then the overridable operation and logic flag, then the external objects flag.</summary>
    private static byte[] ReadInnerTagExtensions(AssetBinaryReader reader)
    {
        if (reader.Asset.ObjectVersionUE5 < ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION) return null;
        long start = reader.BaseStream.Position;
        var extensions = (EPropertyTagExtension)reader.ReadByte();
        if (extensions.HasFlag(EPropertyTagExtension.OverridableInformation)) { reader.ReadByte(); reader.ReadBooleanInt(); }
        if (extensions.HasFlag(EPropertyTagExtension.HasExternalsObjects)) reader.ReadBooleanInt();
        long length = reader.BaseStream.Position - start;
        reader.BaseStream.Position = start;
        return reader.ReadBytes((int)length);
    }

    internal bool ShouldSerializeStructsDifferently = true;

    public bool ShouldSerializeDummyStruct()
    {
        return Value.Length == 0;
    }

    public ArrayPropertyData(FName name) : base(name)
    {
        Value = [];
    }

    public ArrayPropertyData()
    {
        Value = [];
    }

    private static readonly FString CurrentPropertyType = new FString("ArrayProperty");
    public override FString PropertyType => CurrentPropertyType;

    /// <summary>
    /// A large array of a fixed-size native math struct (FVector3f and the like), kept as its packed elements instead of one
    /// struct property per element: ML Deformer models hold millions of morph target deltas.
    /// </summary>
    [JsonProperty] public byte[] PackedElements;
    [JsonProperty] public int PackedCount;

    const int PackThreshold = 65536;

    static int PackedElementSize(string structType, UAsset asset)
    {
        bool lwc = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;
        return structType switch
        {
            "Vector3f" or "Rotator3f" or "IntVector" => 12,
            "Vector2f" or "IntPoint" => 8,
            "Vector4f" or "Quat4f" or "LinearColor" => 16,
            "Color" => 4,
            "Vector" or "Rotator" => lwc ? 24 : 12,
            "Vector2D" => lwc ? 16 : 8,
            "Vector4" or "Quat" => lwc ? 32 : 16,
            _ => 0,
        };
    }

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader && !reader.Asset.HasUnversionedProperties)
        {
            if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME)
            {
                if (PropertyTypeName is null) throw new FormatException("PropertyTypeName is required to read ArrayProperty with complete type names.");
                ArrayType = PropertyTypeName.GetParameter(0).GetName();
            }
            else
            {
                ArrayType = reader.ReadFName();
            }

            this.ReadEndPropertyTag(reader);
        }

        // an array inside another property, such as an optional's value, has no tag of its own; its type name carries the inner type
        if (ArrayType == null && PropertyTypeName != null && PropertyTypeName.GetParameter(0) != null) ArrayType = PropertyTypeName.GetParameter(0).GetName();

        FName arrayStructType = null;
        // before VER_UE4_INNER_ARRAY_TAG_INFO a struct array names no element struct, and a set's elements never carry an
        // inner tag, so before PROPERTY_TAG_COMPLETE_TYPE_NAME the mappings supply it
        bool needsStructType = ArrayType?.Value?.Value == "StructProperty" && (reader.Asset.ObjectVersion < ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO || !ShouldSerializeStructsDifferently);
        if (reader.Asset.Mappings != null && (ArrayType == null || needsStructType) && reader.Asset.Mappings.TryGetPropertyData(Name, Ancestry, reader.Asset, out UsmapArrayData strucDat1))
        {
            ArrayType ??= FName.DefineDummy(reader.Asset, strucDat1.InnerType.Type.ToString());
            if (strucDat1.InnerType is UsmapStructData strucDat2) arrayStructType = FName.DefineDummy(reader.Asset, strucDat2.StructType);
        }

        if (reader.Asset.HasUnversionedProperties && ArrayType == null)
        {
            throw new InvalidOperationException("Unable to determine array type for array " + Name.Value.Value + " in class " + Ancestry.Parent.Value.Value);
        }

        int numEntries = reader.ReadInt32();
        if (numEntries > (reader.BaseStream.Length - reader.BaseStream.Position)) throw new InvalidOperationException("Invalid number of entries (" + numEntries + ") for array " + Name.Value.Value + " in class " + Ancestry.Parent.Value.Value);
        if (ArrayType.Value.Value == "StructProperty" && ShouldSerializeStructsDifferently && !reader.Asset.HasUnversionedProperties)
        {
            var results = new PropertyData[numEntries];

            FName name = this.Name;
            long structLength = 1;
            bool hasInnerLength = false;
            FName fullType = FName.DefineDummy(reader.Asset, "Generic");
            Guid structGUID = new Guid();

            bool isSpecialCase = false;
            if (reader.Asset is UAsset asset)
            {
                isSpecialCase = reader.Asset.ObjectVersion == ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO && asset.WillSerializeNameHashes == true;
            }

            if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME)
            {
                fullType = PropertyTypeName.GetParameter(0).GetParameter(0).GetName();
            }
            else if (reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO && !isSpecialCase)
            {
                name = reader.ReadFName();
                if (name.Value.Value.Equals("None"))
                {
                    reader.Asset.Coverage.Report("untyped-array", $"{Name}: the inner property name is {name}; its header and {numEntries} struct array elements are not decoded.");
                    Value = results;
                    return;
                }

                FName thisArrayType = reader.ReadFName();
                if (thisArrayType.Value.Value.Equals("None"))
                {
                    reader.Asset.Coverage.Report("untyped-array", $"{Name}: the inner property type is {thisArrayType}; its header and {numEntries} struct array elements are not decoded.");
                    Value = results;
                    return;
                }

                if (thisArrayType.Value.Value != ArrayType.Value.Value) throw new FormatException("Invalid array type: " + thisArrayType.ToString() + " vs " + ArrayType.ToString());
                if (thisArrayType.Number != ArrayType.Number)
                    reader.Asset.Coverage.Report("discarded-value", $"{Name}: inner array type {thisArrayType} has a different numeric suffix from {ArrayType}; the inner suffix is not retained.");

                structLength = reader.ReadInt64(); // length value
                hasInnerLength = true;
                if ((structLength >> 32) != 0)
                    reader.Asset.Coverage.Report("discarded-value", $"{Name}: inner array tag index {structLength >> 32} is consumed as part of the length word rather than retained as an index.");
                fullType = reader.ReadFName();
                structGUID = reader.ReadGuid();
                if (reader.ReadPropertyGuid() is Guid)
                    reader.Asset.Coverage.Report("discarded-value", $"{Name}: inner array property GUID was read without retaining it.");
                InnerTagExtensions = ReadInnerTagExtensions(reader);
            }
            else
            {
                // override using mappings or ArrayStructTypeOverride if applicable
                if (arrayStructType != null)
                {
                    fullType = arrayStructType;
                }
                else if (reader.Asset.ArrayStructTypeOverride.ContainsKey(this.Name.Value.Value))
                {
                    fullType = FName.DefineDummy(reader.Asset, reader.Asset.ArrayStructTypeOverride[this.Name.Value.Value]);
                }
            }

            long elementsStart = reader.BaseStream.Position;
            void CheckInnerLength()
            {
                if (hasInnerLength && reader.BaseStream.Position - elementsStart != unchecked((int)structLength))
                    reader.Asset.Coverage.Report("array-size-mismatch", $"{Name}: inner tag declares {unchecked((int)structLength)} element bytes; decoder consumed {reader.BaseStream.Position - elementsStart}.");
            }
            if (numEntries == 0)
            {
                DummyStruct = new StructPropertyData(name, fullType)
                {
                    StructGUID = structGUID
                };
            }
            else if (numEntries > PackThreshold && PackedElementSize(fullType.Value.Value, reader.Asset) is > 0 and var size)
            {
                DummyStruct = new StructPropertyData(name, fullType) { StructGUID = structGUID };
                PackedCount = numEntries;
                PackedElements = reader.ReadBytes(checked(numEntries * size));
                Value = [];
                CheckInnerLength();
                return;
            }
            else
            {
                var propTypeName = PropertyTypeName?.GetParameter(0);
                for (int i = 0; i < numEntries; i++)
                {
                    var data = new StructPropertyData(name, fullType);
                    data.Offset = reader.BaseStream.Position;
                    data.Ancestry.Initialize(Ancestry, Name);
                    data.PropertyTypeName = propTypeName;
                    data.Read(reader, false, structLength, 0, PropertySerializationContext.Array);
                    data.StructGUID = structGUID;
                    results[i] = data;
                }
                DummyStruct = (StructPropertyData)results[0];
            }
            Value = results;
            CheckInnerLength();
        }
        else
        {
            if (numEntries == 0)
            {
                Value = [];
                return;
            }

            int primitive = ArrayType.Value.Value switch
            {
                "FloatProperty" or "IntProperty" or "UInt32Property" => 4,
                "DoubleProperty" or "Int64Property" or "UInt64Property" => 8,
                "Int16Property" or "UInt16Property" => 2,
                "Int8Property" => 1,
                _ => 0,
            };
            if (numEntries > PackThreshold && primitive > 0 && !reader.Asset.HasUnversionedProperties)
            {
                PackedCount = numEntries;
                PackedElements = reader.ReadBytes(checked(numEntries * primitive));
                Value = [];
                return;
            }

            int averageSizeEstimate = (int)((leng1 - 4) / numEntries);
            if (averageSizeEstimate <= 0) averageSizeEstimate = 1; // missing possible edge case where leng1 = 4 and numEntries = 2, may result is wrong size

            var propTypeName = PropertyTypeName?.GetParameter(0);
            var results = new PropertyData[numEntries];
            for (int i = 0; i < numEntries; i++)
            {
                results[i] = MainSerializer.TypeToClass(ArrayType, FName.DefineDummy(reader.Asset, i.ToString(), int.MinValue), Ancestry, Name, null, reader.Asset, propertyTypeName: propTypeName);
                results[i].Offset = reader.BaseStream.Position;
                if (results[i] is StructPropertyData data) data.StructType = arrayStructType ?? propTypeName?.GetParameter(0)?.GetName() ?? FName.DefineDummy(reader.Asset, "Generic");
                results[i].Read(reader, false, averageSizeEstimate, 0, PropertySerializationContext.Array);
            }

            Value = results;
        }
    }

    public override void ResolveAncestries(UAsset asset, AncestryInfo ancestrySoFar)
    {
        var ancestryNew = (AncestryInfo)ancestrySoFar.Clone();
        ancestryNew.SetAsParent(Name);

        if (Value != null)
        {
            for (int i = 0; i < Value.Length; i++) Value[i].ResolveAncestries(asset, ancestryNew);
        }
        base.ResolveAncestries(asset, ancestrySoFar);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (Value == null) Value = [];

        // let's get our ArrayType if we don't already know it
        // if Value has entries, take it from there
        if (Value.Length > 0)
        {
            ArrayType = writer.Asset.HasUnversionedProperties ? FName.DefineDummy(writer.Asset, Value[0].PropertyType) : new FName(writer.Asset, Value[0].PropertyType);
        }

        // otherwise, let's check mappings
        FName arrayStructType = null;
        if (writer.Asset.Mappings != null && ArrayType == null && writer.Asset.Mappings.TryGetPropertyData(Name, Ancestry, writer.Asset, out UsmapArrayData strucDat1))
        {
            ArrayType = FName.DefineDummy(writer.Asset, strucDat1.InnerType.Type.ToString());
            if (strucDat1.InnerType is UsmapStructData strucDat2) arrayStructType = FName.DefineDummy(writer.Asset, strucDat2.StructType);
        }

        // at this point, if we couldn't get our ArrayType and we're using unversioned properties, we can't continue; otherwise, not needed
        if (writer.Asset.HasUnversionedProperties && ArrayType == null)
        {
            throw new InvalidOperationException("Unable to determine array type for array " + Name.Value.Value + " in class " + Ancestry.Parent.Value.Value);
        }

        // begin actual serialization
        if (includeHeader && !writer.Asset.HasUnversionedProperties)
        {
            if (writer.Asset.ObjectVersionUE5 < ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME)
            {
                writer.Write(ArrayType);
            }
            this.WriteEndPropertyTag(writer);
        }

        int here = (int)writer.BaseStream.Position;
        writer.Write(PackedElements != null ? PackedCount : Value.Length);
        if (ArrayType?.Value?.Value == "StructProperty" && ShouldSerializeStructsDifferently && !writer.Asset.HasUnversionedProperties)
        {
            if (Value.Length == 0 && DummyStruct == null && PackedElements == null)
            {
                // let's try and reconstruct DummyStruct, if we can
                if (arrayStructType == null && writer.Asset.ArrayStructTypeOverride.ContainsKey(this.Name.Value.Value))
                {
                    arrayStructType = FName.DefineDummy(writer.Asset, writer.Asset.ArrayStructTypeOverride[this.Name.Value.Value]);
                }

                if (arrayStructType == null)
                {
                    throw new InvalidOperationException("DummyStruct is null within empty StructProperty array \"" + Name.Value.Value + "\" in class \"" + Ancestry.Parent.Value.Value + "\"");
                }

                DummyStruct = new StructPropertyData(this.Name, arrayStructType)
                {
                    StructGUID = Guid.Empty // not sure what else to use here...
                };
            }
            if (Value.Length > 0) DummyStruct = (StructPropertyData)Value[0];

            FName fullType = DummyStruct.StructType;

            int lengthLoc = -1;

            bool isSpecialCase = false;
            if (writer.Asset is UAsset)
            {
                isSpecialCase = writer.Asset.ObjectVersion == ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO && ((UAsset)writer.Asset).WillSerializeNameHashes == true;
            }

            if (writer.Asset.ObjectVersionUE5 < ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME && writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO && !isSpecialCase)
            {
                writer.Write(DummyStruct.Name);
                writer.Write(new FName(writer.Asset, "StructProperty"));
                lengthLoc = (int)writer.BaseStream.Position;
                writer.Write((long)0);
                writer.Write(fullType);
                if (writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_STRUCT_GUID_IN_PROPERTY_TAG) writer.Write(DummyStruct.StructGUID);
                if (writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_PROPERTY_GUID_IN_PROPERTY_TAG) writer.Write((byte)0);
                if (writer.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION) writer.Write(InnerTagExtensions ?? new byte[1]);
            }

            if (PackedElements != null) writer.Write(PackedElements);
            for (int i = 0; i < Value.Length; i++)
            {
                ((StructPropertyData)Value[i]).StructType = fullType;
                Value[i].Offset = writer.BaseStream.Position;
                Value[i].Write(writer, false);
            }

            if (writer.Asset.ObjectVersionUE5 < ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME && writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_INNER_ARRAY_TAG_INFO && !isSpecialCase)
            {
                int fullLen = (int)writer.BaseStream.Position - lengthLoc;
                int newLoc = (int)writer.BaseStream.Position;
                writer.Seek(lengthLoc, SeekOrigin.Begin);
                int extensionLength = writer.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION ? (InnerTagExtensions?.Length ?? 1) : 0;
                writer.Write(fullLen - 32 - (includeHeader ? 1 : 0) - extensionLength);
                writer.Seek(newLoc, SeekOrigin.Begin);
            }
        }
        else
        {
            if (PackedElements != null) writer.Write(PackedElements);
            for (int i = 0; i < Value.Length; i++)
            {
                if (serializationContext == PropertySerializationContext.CanBeZero && ((CanBeZeroStream)writer.BaseStream).HasWrittenNonZero) return -1;
                Value[i].Offset = writer.BaseStream.Position;
                Value[i].Write(writer, false, PropertySerializationContext.Array);
            }
        }

        return (int)writer.BaseStream.Position - here;
    }

    public override void FromString(string[] d, UAsset asset)
    {
        if (d[4] != null) ArrayType = FName.FromString(asset, d[4]);
        if (!string.IsNullOrWhiteSpace(d[0]))
        {
            DummyStruct = d[0] == FString.NullCase ? null : new StructPropertyData(this.Name, FName.FromString(asset, d[0]))
            {
                StructGUID = DummyStruct?.StructGUID ?? Guid.Empty
            };
        }
    }

    protected override void HandleCloned(PropertyData res)
    {
        ArrayPropertyData cloningProperty = (ArrayPropertyData)res;
        cloningProperty.ArrayType = (FName)this.ArrayType?.Clone();
        cloningProperty.DummyStruct = (StructPropertyData)this.DummyStruct?.Clone();
    }
}
