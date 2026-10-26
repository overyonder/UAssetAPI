using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>
/// A double-precision vector (FVector3d), a core variant serialized natively as X, Y, Z. It exists only in packages with
/// large world coordinates, where FVector is read as doubles.
/// </summary>
public class Vector3dPropertyData : BasePropertyData<FVector>
{
    public Vector3dPropertyData(FName name) : base(name) { }

    public Vector3dPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Vector3d");
    public override FString PropertyType => CurrentPropertyType;
}
