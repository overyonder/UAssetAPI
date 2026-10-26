using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>An FTransform3f, serialized natively as a quaternion, a translation and a scale, all single precision.</summary>
public class Transform3fPropertyData : BasePropertyData<FTransform3f>
{
    public Transform3fPropertyData(FName name) : base(name) { }

    public Transform3fPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("Transform3f");
    public override FString PropertyType => CurrentPropertyType;
}
