using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

public class MovieSceneDoubleValuePropertyData : PropertyData<FMovieSceneValue<double>>
{
    public MovieSceneDoubleValuePropertyData(FName name) : base(name) { }

    public MovieSceneDoubleValuePropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("MovieSceneDoubleValue");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader)
        {
            this.ReadEndPropertyTag(reader);
        }

        // a float before large world coordinates, converted on load
        Value = reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES
            ? MovieSceneSerializedValue.Read(reader, reader.ReadDouble) : MovieSceneSerializedValue.Read<double>(reader, () => reader.ReadSingle());
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader)
        {
            this.WriteEndPropertyTag(writer);
        }

        var offset = writer.BaseStream.Position;

        if (writer.Asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES) MovieSceneSerializedValue.Write<double>(writer, Value, writer.Write);
        else MovieSceneSerializedValue.Write<double>(writer, Value, d => writer.Write((float)d));

        return (int)(writer.BaseStream.Position - offset);
    }
}
