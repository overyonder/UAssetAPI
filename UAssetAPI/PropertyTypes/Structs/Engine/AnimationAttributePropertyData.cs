using System.Collections.Generic;
using Newtonsoft.Json;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>Animation/AnimData/AttributeIdentifier.h FAnimationAttributeIdentifier::Serialize: name, bone name, bone index, attribute struct path.</summary>
public class AnimationAttributeIdentifierPropertyData : PropertyData
{
    [JsonProperty] public FName AttributeName;
    [JsonProperty] public FName BoneName;
    [JsonProperty] public int BoneIndex;
    [JsonProperty] public FSoftObjectPath ScriptStructPath;

    public AnimationAttributeIdentifierPropertyData(FName name) : base(name) { }
    public AnimationAttributeIdentifierPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("AnimationAttributeIdentifier");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        AttributeName = reader.ReadFName();
        BoneName = reader.ReadFName();
        BoneIndex = reader.ReadInt32();
        ScriptStructPath = new FSoftObjectPath(reader);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        writer.Write(AttributeName);
        writer.Write(BoneName);
        writer.Write(BoneIndex);
        ScriptStructPath.Write(writer);
        return (int)(writer.BaseStream.Position - start);
    }
}

/// <summary>
/// Animation/AttributeCurve.cpp FAttributeCurve::Serialize: the key times, the attribute struct path and, when it is set,
/// each key's value serialized by that struct (its tagged properties).
/// </summary>
public class AttributeCurvePropertyData : PropertyData
{
    [JsonProperty] public List<float> KeyTimes;
    [JsonProperty] public FSoftObjectPath ScriptStructPath;
    [JsonProperty] public List<StructPropertyData> KeyValues;

    public AttributeCurvePropertyData(FName name) : base(name) { }
    public AttributeCurvePropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("AttributeCurve");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    /// <summary>The attribute struct's name, the last element of its path, or null for a null path.</summary>
    string StructName()
    {
        string path = ScriptStructPath.AssetPath.AssetName?.ToString();
        if (string.IsNullOrEmpty(path) || path == "None") path = ScriptStructPath.AssetPath.PackageName?.ToString();
        if (string.IsNullOrEmpty(path) || path == "None") return null;
        int dot = path.LastIndexOf('.');
        return dot >= 0 ? path[(dot + 1)..] : path;
    }

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        int n = reader.ReadInt32();
        if (n < 0 || (long)n * 4 > reader.BaseStream.Length - reader.BaseStream.Position) throw new System.FormatException($"Invalid attribute key count {n}");
        KeyTimes = new List<float>(n);
        for (int i = 0; i < n; i++) KeyTimes.Add(reader.ReadSingle());
        ScriptStructPath = new FSoftObjectPath(reader);
        string structName = StructName();
        if (structName == null) return;
        KeyValues = new List<StructPropertyData>(n);
        for (int i = 0; i < n; i++)
        {
            var value = new StructPropertyData(Name, FName.DefineDummy(reader.Asset, structName));
            value.Ancestry.Initialize(Ancestry, Name);
            value.Read(reader, false, 1, 0, PropertySerializationContext.StructFallback);
            KeyValues.Add(value);
        }
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        writer.Write(KeyTimes.Count);
        foreach (var t in KeyTimes) writer.Write(t);
        ScriptStructPath.Write(writer);
        if (KeyValues != null) foreach (var v in KeyValues) v.Write(writer, false, PropertySerializationContext.StructFallback);
        return (int)(writer.BaseStream.Position - start);
    }
}
