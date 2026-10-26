using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

public class MovieSceneFloatValuePropertyData : PropertyData<FMovieSceneValue<float>>
{
    public MovieSceneFloatValuePropertyData(FName name) : base(name) { }

    public MovieSceneFloatValuePropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("MovieSceneFloatValue");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader)
        {
            this.ReadEndPropertyTag(reader);
        }

        Value = MovieSceneSerializedValue.Read(reader, reader.ReadSingle);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader)
        {
            this.WriteEndPropertyTag(writer);
        }

        var offset = writer.BaseStream.Position;

        MovieSceneSerializedValue.Write<float>(writer, Value, writer.Write);

        return (int)(writer.BaseStream.Position - offset);
    }
}
