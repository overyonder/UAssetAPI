using System;
using UAssetAPI.PropertyTypes.Objects;

namespace UAssetAPI.UnrealTypes;

/// <summary>
/// A single-precision transform (FTransform3f). Math/TransformNonVectorized.h serializes it natively as its rotation,
/// translation and scale; Class.h declares it a core variant, which gives it that serializer.
/// </summary>
public struct FTransform3f : ICloneable, IStruct<FTransform3f>
{
    public FVector4f Rotation;
    public FVector3f Translation;
    public FVector3f Scale3D;

    public FTransform3f(AssetBinaryReader reader)
    {
        Rotation = FVector4f.Read(reader);
        Translation = FVector3f.Read(reader);
        Scale3D = FVector3f.Read(reader);
    }

    public int Write(AssetBinaryWriter writer) => Rotation.Write(writer) + Translation.Write(writer) + Scale3D.Write(writer);

    public static FTransform3f Read(AssetBinaryReader reader) => new FTransform3f(reader);

    public override string ToString() => $"({Rotation}, {Translation}, {Scale3D})";

    public static FTransform3f FromString(string[] d, UAsset asset) => throw new NotSupportedException("FTransform3f has no string form");

    public object Clone() => this;
}
