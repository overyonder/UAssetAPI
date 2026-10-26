using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>GameplayAbilities GameplayEffect.cpp FGameplayEffectVersion::Serialize: CurrentVersion, an EGameplayEffectVersion byte.</summary>
public class GameplayEffectVersionPropertyData : PropertyData<byte>
{
    public GameplayEffectVersionPropertyData(FName name) : base(name) { }
    public GameplayEffectVersionPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("GameplayEffectVersion");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.ReadEndPropertyTag(reader);
        Value = reader.ReadByte();
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        if (includeHeader) this.WriteEndPropertyTag(writer);
        writer.Write(Value);
        return sizeof(byte);
    }
}
