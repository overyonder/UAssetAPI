using System;
using Newtonsoft.Json;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// MovieScene Variants/MovieSceneTimeWarpVariant.cpp FMovieSceneTimeWarpVariant::Serialize through
/// FMovieSceneNumericVariant::SerializeCustom: whether it is a literal play rate; a literal is a double, otherwise an
/// EMovieSceneTimeWarpType byte and its data: a custom getter object, or the tagged properties of the fixed frame, frame
/// rate (native), loop, clamp, float loop or float clamp struct.
/// </summary>
public class MovieSceneTimeWarpVariantPropertyData : PropertyData
{
    static readonly string[] TypeStructs = [null, null, "MovieSceneTimeWarpFixedFrame", "FrameRate", "MovieSceneTimeWarpLoop",
        "MovieSceneTimeWarpClamp", "MovieSceneTimeWarpLoopFloat", "MovieSceneTimeWarpClampFloat"];
    const byte Custom = 1, FrameRateType = 3;

    [JsonProperty] public bool IsLiteral;
    [JsonProperty] public double Literal;
    [JsonProperty] public byte Type;
    [JsonProperty] public FPackageIndex CustomGetter;
    [JsonProperty] public StructPropertyData Data;
    /// <summary>The frame rate, which FFrameRate's native serializer writes as numerator and denominator.</summary>
    [JsonProperty] public FrameRatePropertyData FrameRate;

    public MovieSceneTimeWarpVariantPropertyData(FName name) : base(name) { }
    public MovieSceneTimeWarpVariantPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("MovieSceneTimeWarpVariant");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        uint literal = reader.ReadUInt32();
        if (literal > 1) throw new FormatException($"Invalid time warp literal flag {literal}");
        IsLiteral = literal == 1;
        if (IsLiteral) { Literal = reader.ReadDouble(); return; }
        Type = reader.ReadByte();
        if (Type == Custom) { CustomGetter = new FPackageIndex(reader); return; }
        if (Type == FrameRateType) { FrameRate = new FrameRatePropertyData(Name); FrameRate.Read(reader, false, 8); return; }
        if (Type >= TypeStructs.Length || TypeStructs[Type] == null) throw new FormatException($"Invalid time warp type {Type}");
        Data = new StructPropertyData(Name, FName.DefineDummy(reader.Asset, TypeStructs[Type]));
        Data.Ancestry.Initialize(Ancestry, Name);
        Data.Read(reader, false, 1, 0, PropertySerializationContext.StructFallback);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        long start = writer.BaseStream.Position;
        writer.Write(IsLiteral ? 1u : 0u);
        if (IsLiteral) writer.Write(Literal);
        else
        {
            writer.Write(Type);
            if (Type == Custom) CustomGetter.Write(writer);
            else if (Type == FrameRateType) FrameRate.Write(writer, false);
            else Data.Write(writer, false, PropertySerializationContext.StructFallback);
        }
        return (int)(writer.BaseStream.Position - start);
    }
}
