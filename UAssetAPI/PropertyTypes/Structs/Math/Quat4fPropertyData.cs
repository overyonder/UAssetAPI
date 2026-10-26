using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// A single-precision quaternion (FQuat4f), stored as four floats X, Y, Z, W, the same layout as FVector4f.
/// </summary>
public class Quat4fPropertyData : BasePropertyData<FVector4f>
{
    public Quat4fPropertyData(FName name) : base(name) { }

    public Quat4fPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Quat4f");
    public override FString PropertyType => CurrentPropertyType;
}
