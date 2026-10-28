using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.PropertyTypes.Objects;

/// <summary>
/// Describes an enumeration value.
/// </summary>
public class EnumPropertyData : PropertyData<FName>
{
    [JsonProperty]
    public FName EnumType;
    /// <summary>
    /// Only used with unversioned properties.
    /// </summary>
    [JsonProperty]
    public FName InnerType;

    public EnumPropertyData(FName name) : base(name) { }

    public EnumPropertyData() { }

    private static readonly List<string> ValidEnumInnerTypeList = ["ByteProperty", "UInt16Property", "UInt32Property", "UInt64Property", "Int8Property", "Int16Property", "IntProperty", "Int64Property"];

    private static readonly FString CurrentPropertyType = new FString("EnumProperty");
    public override FString PropertyType { get { return CurrentPropertyType; } }

    public static readonly string InvalidEnumIndexFallbackPrefix = "UASSETAPI_INVALID_ENUM_IDX_";

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        // a tagged property names its enum in the tag; only unversioned properties take it from the mappings
        if (reader.Asset.HasUnversionedProperties && reader.Asset.Mappings != null && reader.Asset.Mappings.TryGetPropertyData(Name, Ancestry, reader.Asset, out UsmapEnumData enumDat1))
        {
            EnumType = reader.Asset.HasUnversionedProperties ? FName.DefineDummy(reader.Asset, enumDat1.Name) : new FName(reader.Asset, enumDat1.Name);
            InnerType = reader.Asset.HasUnversionedProperties ? FName.DefineDummy(reader.Asset, enumDat1.InnerType.Type.ToString()) : new FName(reader.Asset, enumDat1.Name);
            if (reader.Asset.Mappings.EnumMap.TryGetValue(enumDat1.Name, out UsmapEnum mappedEnum) && mappedEnum.IsPlaceholder)
            {
                reader.Asset.MissingEnumMappings.Add(enumDat1.Name);
                reader.Asset.Coverage.Report("unresolved-enum", $"{Name}: enum {enumDat1.Name} has a placeholder mapping; its declared members are unavailable.");
            }
        }

        if (reader.Asset.HasUnversionedProperties && serializationContext.IsNormal())
        {
            Value = null;
            if (InnerType?.Value.Value is "ByteProperty" or "UInt16Property" or "UInt32Property" or "UInt64Property")
            {
                long enumIndice = 0;

                switch (InnerType?.Value.Value)
                {
                    case "ByteProperty":
                        enumIndice = reader.ReadByte();
                        break;
                    case "UInt16Property":
                        enumIndice = reader.ReadUInt16();
                        break;
                    case "UInt32Property":
                        enumIndice = reader.ReadUInt32();
                        break;
                    case "UInt64Property":
                        enumIndice = unchecked((long)reader.ReadUInt64());
                        break;
                }

                var enumMapping = reader.Asset.Mappings.EnumMap[EnumType.Value.Value];
                enumMapping.ReportValueInspectionFailure(reader.Asset);
                var listOfValues = enumMapping.Values;
                if (listOfValues.TryGetValue(enumIndice, out string mappedName) && !string.IsNullOrEmpty(mappedName))
                {
                    Value = FName.DefineDummy(reader.Asset, listOfValues[enumIndice]);
                    ReportAmbiguousValue(reader, listOfValues, enumIndice);
                }
                else
                {
                    // fallback
                    Value = FName.DefineDummy(reader.Asset, InvalidEnumIndexFallbackPrefix + enumIndice.ToString());
                    reader.Asset.Coverage.Report("unresolved-enum", $"{Name}: enum {EnumType} has no nonempty mapped member for {enumIndice}; a numeric fallback label is emitted.");
                }
                return;
            }
            if (InnerType?.Value.Value == "Int8Property" || InnerType?.Value.Value == "Int16Property" ||
                InnerType?.Value.Value == "IntProperty" || InnerType?.Value.Value == "Int64Property")
            {
                long enumIndice = 0;

                switch (InnerType?.Value.Value)
                {
                    case "Int8Property":
                        enumIndice = reader.ReadSByte();
                        break;
                    case "Int16Property":
                        enumIndice = reader.ReadInt16();
                        break;
                    case "IntProperty":
                        enumIndice = reader.ReadInt32();
                        break;
                    case "Int64Property":
                        enumIndice = reader.ReadInt64();
                        break;
                }

                var enumMapping = reader.Asset.Mappings.EnumMap[EnumType.Value.Value];
                enumMapping.ReportValueInspectionFailure(reader.Asset);
                var listOfValues = enumMapping.Values;
                if (listOfValues.TryGetValue(enumIndice, out string mappedName) && !string.IsNullOrEmpty(mappedName))
                {
                    Value = FName.DefineDummy(reader.Asset, listOfValues[enumIndice]);
                    ReportAmbiguousValue(reader, listOfValues, enumIndice);
                }
                else
                {
                    // fallback
                    Value = FName.DefineDummy(reader.Asset, InvalidEnumIndexFallbackPrefix + enumIndice.ToString());
                    reader.Asset.Coverage.Report("unresolved-enum", $"{Name}: enum {EnumType} has no nonempty mapped member for {enumIndice}; a numeric fallback label is emitted.");
                }
                return;
            }
            reader.Asset.Coverage.Report("unsupported-enum-underlying-type", $"{Name}: no unversioned numeric enum reader handles mapped underlying type {InnerType}; the fallback FName representation is read instead.");
        }

        if (includeHeader && !reader.Asset.HasUnversionedProperties)
        {
            if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME)
            {
                if (PropertyTypeName is null) throw new FormatException("PropertyTypeName is required to read EnumProperty with complete type names.");
                EnumType = PropertyTypeName.GetParameter(0).GetName();
            }
            else
            {
                EnumType = reader.ReadFName();
            }
            this.ReadEndPropertyTag(reader);
        }

        Value = reader.ReadFName();
    }

    void ReportAmbiguousValue(AssetBinaryReader reader, IDictionary<long, string> values, long index)
    {
        if (values.Any(entry => entry.Key != index && entry.Value == values[index]))
            reader.Asset.Coverage.Report("ambiguous-enum-output", $"{Name}: enum {EnumType} maps numeric value {index} and another value to the same name {values[index]}; decoded output retains the name without the original numeric value.");
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        // a tagged property names its enum in the tag; only unversioned properties take it from the mappings
        if (writer.Asset.HasUnversionedProperties && writer.Asset.Mappings != null && writer.Asset.Mappings.TryGetPropertyData(Name, Ancestry, writer.Asset, out UsmapEnumData enumDat1))
        {
            EnumType = writer.Asset.HasUnversionedProperties ? FName.DefineDummy(writer.Asset, enumDat1.Name) : new FName(writer.Asset, enumDat1.Name);
            InnerType = writer.Asset.HasUnversionedProperties ? FName.DefineDummy(writer.Asset, enumDat1.InnerType.Type.ToString()) : new FName(writer.Asset, enumDat1.Name);
        }

        if (writer.Asset.HasUnversionedProperties && serializationContext.IsNormal())
        {
            if (ValidEnumInnerTypeList.Contains(InnerType?.Value?.Value))
            {
                long enumIndice = 0;
                var listOfEnums = writer.Asset.Mappings.EnumMap[EnumType.Value.Value].Values;
                var validIndices = listOfEnums.Where(pair => pair.Value == Value?.Value?.Value).Select(pair => pair.Key);
                if (Value == null)
                {
                    enumIndice = -1;
                }
                else if (validIndices.Count() == 0)
                {
                    bool success = false;
                    if (Value.Value.Value.StartsWith(InvalidEnumIndexFallbackPrefix))
                    {
                        success = long.TryParse(Value.Value.Value.Substring(InvalidEnumIndexFallbackPrefix.Length), out enumIndice);
                    }

                    if (!success)
                    {
                        throw new FormatException("Could not serialize EnumProperty value " + Value.Value.Value + " as " + InnerType?.Value?.Value);
                    }
                }
                else
                {
                    enumIndice = validIndices.FirstOrDefault();
                }

                switch (InnerType?.Value?.Value)
                {
                    case "ByteProperty":
                        writer.Write((byte)enumIndice);
                        return sizeof(byte);
                    case "UInt16Property":
                        writer.Write((ushort)enumIndice);
                        return sizeof(ushort);
                    case "UInt32Property":
                        writer.Write((uint)enumIndice);
                        return sizeof(uint);
                    case "UInt64Property":
                        writer.Write(unchecked((ulong)enumIndice));
                        return sizeof(ulong);
                    case "Int8Property":
                        writer.Write((sbyte)enumIndice);
                        return sizeof(sbyte);
                    case "Int16Property":
                        writer.Write((short)enumIndice);
                        return sizeof(short);
                    case "IntProperty":
                        writer.Write((int)enumIndice);
                        return sizeof(int);
                    case "Int64Property":
                        writer.Write((long)enumIndice);
                        return sizeof(long);
                }
            }
        }

        if (includeHeader && !writer.Asset.HasUnversionedProperties)
        {
            if (writer.Asset.ObjectVersionUE5 < ObjectVersionUE5.PROPERTY_TAG_COMPLETE_TYPE_NAME)
            {
                writer.Write(EnumType);
            }

            this.WriteEndPropertyTag(writer);
        }
        writer.Write(Value);
        return sizeof(int) * 2;
    }

    internal override void InitializeZero(AssetBinaryReader reader)
    {
        if (reader.Asset.Mappings.TryGetPropertyData(Name, Ancestry, reader.Asset, out UsmapEnumData enumDat1))
        {
            EnumType = FName.DefineDummy(reader.Asset, enumDat1.Name);
            InnerType = FName.DefineDummy(reader.Asset, enumDat1.InnerType.Type.ToString());
        }

        // fill in data for enumIndice = 0 to provide clarity for end-user
        if (ValidEnumInnerTypeList.Contains(InnerType?.Value?.Value))
        {
            long enumIndice = 0;
            var enumMapping = reader.Asset.Mappings.EnumMap[EnumType.Value.Value];
            enumMapping.ReportValueInspectionFailure(reader.Asset);
            var listOfValues = enumMapping.Values;
            if (listOfValues.TryGetValue(enumIndice, out string mappedName) && !string.IsNullOrEmpty(mappedName))
            {
                Value = FName.DefineDummy(reader.Asset, listOfValues[enumIndice]);
            }
            else
            {
                // fallback
                Value = FName.DefineDummy(reader.Asset, InvalidEnumIndexFallbackPrefix + enumIndice.ToString());
                reader.Asset.Coverage.Report("unresolved-enum", $"{Name}: enum {EnumType} has no nonempty mapped member for its zero value; a numeric fallback label is emitted.");
            }
        }
        else reader.Asset.Coverage.Report("unsupported-enum-underlying-type", $"{Name}: the zero value for mapped enum underlying type {InnerType} is not initialized by a supported numeric enum reader.");
    }

    public override string ToString()
    {
        return Value.ToString();
    }

    // note: Value must be overridden manually after this is called in cases where !serializationContext.IsNormal(), to ensure not dummy
    public override void FromString(string[] d, UAsset asset)
    {
        if (d[0] != "null" && d[0] != null)
        {
            EnumType = asset.HasUnversionedProperties ? FName.DefineDummy(asset, d[0]) : FName.FromString(asset, d[0]);
        }
        else
        {
            EnumType = null;
        }

        if (d[1] != "null" && d[1] != null)
        {
            Value = (asset.HasUnversionedProperties && (ValidEnumInnerTypeList.Contains(InnerType?.Value?.Value))) ? FName.DefineDummy(asset, d[1]) : FName.FromString(asset, d[1]);
        }
        else
        {
            Value = null;
        }
    }

    protected override void HandleCloned(PropertyData res)
    {
        EnumPropertyData cloningProperty = (EnumPropertyData)res;
        cloningProperty.EnumType = (FName)this.EnumType?.Clone();
        cloningProperty.InnerType = (FName)this.InnerType?.Clone();
    }
}
