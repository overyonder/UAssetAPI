using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// Chaos ChaosArchive.h FChaosArchive::SerializePtr and the implicit objects it writes: whether the pointer is set, its tag, and
/// on a tag's first appearance the object through ImplicitObject.cpp FImplicitObject::SerializationFactory (its type byte) and
/// that type's Serialize. Later appearances of a tag refer to the object already read. ChaosCore Vector.h SerializeReal
/// writes a Chaos TVector of FReal as floats at every version; a transform goes through FTransform, in doubles from large
/// world coordinates.
/// </summary>
internal sealed class ChaosImplicitSerializer
{
    static readonly Guid DestructionGuid = UAPUtils.GUID(0x174F1F0B, 0xB4C645A5, 0xB13F2EE8, 0xD0FB917D);
    static readonly Guid ReleaseGuid = UAPUtils.GUID(0x9C54D522, 0xA8264FBE, 0x94210746, 0x61B482D0);
    static readonly Guid ExternalPhysicsGuid = UAPUtils.GUID(0x35F94A83, 0xE258406C, 0xA31809F5, 0x9610247C);
    static readonly Guid PhysicsGuid = UAPUtils.GUID(0x78F01B33, 0xEBEA4F98, 0xB9B484EA, 0xCCB95AA2);
    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid UE5ReleaseGuid = UAPUtils.GUID(0xD89B5E42, 0x24BD4D46, 0x8412ACA8, 0xDF641779);
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    static readonly Guid FortniteSeasonGuid = UAPUtils.GUID(0x5B4C06B7, 0x24634AF8, 0x805BBF70, 0xCDF5D0DD);

    static readonly string[] TypeNames = { "Sphere", "Box", "Plane", "Capsule", "Transformed", "Union", "LevelSet", "Unknown", "Convex",
        "TaperedCylinder", "Cylinder", "TriangleMesh", "HeightField", "DEPRECATED_Scaled", "Triangle", "UnionClustered", "TaperedCapsule",
        "MLLevelSet", "SkinnedTriangleMesh", "ExtrudedTaperedCapsule" };

    readonly ClassSerializers.Cursor c;
    readonly AssetBinaryReader r;
    readonly int destruction, release, external, physics, main, ue5release, fortnite, season;
    readonly HashSet<int> seen = new();

    public ChaosImplicitSerializer(ClassSerializers.Cursor c, AssetBinaryReader r, UAsset asset)
    {
        this.c = c; this.r = r;
        destruction = asset.GetCustomVersion(DestructionGuid); release = asset.GetCustomVersion(ReleaseGuid);
        external = asset.GetCustomVersion(ExternalPhysicsGuid); physics = asset.GetCustomVersion(PhysicsGuid);
        main = asset.GetCustomVersion(UE5MainGuid); ue5release = asset.GetCustomVersion(UE5ReleaseGuid);
        fortnite = asset.GetCustomVersion(FortniteMainGuid); season = asset.GetCustomVersion(FortniteSeasonGuid);
    }

    /// <summary>A serializable pointer to an implicit object: null, a reference to an earlier tag, or the tagged object.</summary>
    public object Pointer() => Pointer(Object);

    /// <summary>BVHParticles.cpp FBVHParticles::Serialize, whose factory reads nothing: Particles.h TParticles::Serialize (a set
    /// flag and the positions), then its bounding volume hierarchy.</summary>
    public object BVHParticles() => Pointer(() =>
    {
        var o = new Dictionary<string, object> { ["Type"] = "BVHParticles" };
        if (!c.Bool()) throw new FormatException("Chaos particles saved as a shared view");
        int n = c.Count(12);
        o["X"] = c.Floats(n * 3);
        o["BVH"] = Hierarchy();
        return o;
    });

    object Pointer(Func<Dictionary<string, object>> read)
    {
        if (destruction < 5) throw new FormatException("Chaos object saved before ChaosArchiveAdded");
        long start = r.BaseStream.Position;
        bool exists;
        try { exists = c.Bool(); }
        catch (FormatException e) { throw new FormatException($"pointer {seen.Count} objects in, at {start}: {e.Message}", e); }
        if (!exists) return null;
        int tag = r.ReadInt32();
        if (tag < 0) throw new FormatException($"Invalid Chaos object tag {tag}");
        if (!seen.Add(tag)) return new Dictionary<string, object> { ["Tag"] = tag };
        long at = r.BaseStream.Position;
        Dictionary<string, object> o;
        try { o = read(); }
        catch (FormatException e) when (!e.Message.StartsWith("tag ")) { throw new FormatException($"tag {tag} object from {at}: {e.Message}", e); }
        o["Tag"] = tag;
        return o;
    }

    Dictionary<string, object> Object()
    {
        byte type = r.ReadByte();
        bool scaled = (type & 0x80) != 0 && external >= 12, instanced = (type & 0x40) != 0;
        int concrete = type & 0x1F;
        if ((type & 0x20) != 0) throw new FormatException($"Chaos implicit object weighted lattice {Name(concrete)}");
        var o = new Dictionary<string, object> { ["Type"] = Name(concrete) };
        if (scaled || instanced)
        {
            // ImplicitObjectScaled.h: TImplicitObjectInstanced writes its object; TImplicitObjectScaled its object, scale, inverse
            // scale and local bounds
            if (concrete != 8 && concrete != 11) throw new FormatException($"Chaos implicit object {(scaled ? "scaled" : "instanced")} {Name(concrete)}");
            o["Type"] = (scaled ? "Scaled" : "Instanced") + Name(concrete);
            Base(o);
            o["Object"] = Pointer();
            if (scaled) { o["Scale"] = c.Floats(3); o["InvScale"] = c.Floats(3); o["LocalBoundingBox"] = AABB(); }
            return o;
        }
        Base(o);
        switch (concrete)
        {
            case 0: o["Center"] = c.Floats(3); o["Radius"] = r.ReadSingle(); break;
            case 1: o["Min"] = c.Floats(3); o["Max"] = c.Floats(3); if (release >= 37) o["Margin"] = r.ReadSingle(); break;
            case 2: o["X"] = c.Floats(3); o["Normal"] = c.Floats(3); break;
            case 3:
                o["Point"] = c.Floats(3); o["Axis"] = c.Floats(3); o["Length"] = r.ReadSingle(); o["Radius"] = r.ReadSingle();
                if (external < 30) { o["LegacyBounds"] = AABB(); o["LegacyUnion"] = Pointer(); }
                break;
            case 4:
                // ImplicitObjectTransformed.h: the object, its transform and local bounds
                o["Object"] = Pointer(); o["Transform"] = new object[] { c.Vector4(), c.Vector(), c.Vector() }; o["LocalBoundingBox"] = AABB();
                break;
            case 5: Union(o); break;
            case 8: Convex(o); break;
            default: throw new FormatException($"Chaos implicit object {Name(concrete)}");
        }
        return o;
    }

    static string Name(int type) => type < TypeNames.Length ? TypeNames[type] : $"type {type}";

    // ImplicitObject.cpp FImplicitObject::SerializeImp: convexity and collision flags from ChaosArchiveAdded, the collision type
    // after CustomImplicitCollisionType
    void Base(Dictionary<string, object> o)
    {
        if (destruction >= 5) { o["bIsConvex"] = c.Bool(); o["bDoCollide"] = c.Bool(); }
        if (release > 29) o["CollisionType"] = r.ReadByte();
    }

    // Box.h TBox::SerializeAsAABB: a TAABB from TBoxReplacedWithTAABB
    object AABB()
    {
        if (external < 25) throw new FormatException("Chaos bounds saved before TBoxReplacedWithTAABB");
        return new[] { c.Floats(3), c.Floats(3) };
    }

    // ImplicitObjectUnion.cpp FImplicitObjectUnion::Serialize: children, bounds, then (from ChaosImplicitObjectUnionBVHRefactor) its
    // flags, leaf count and, with a BVH, ImplicitObjectBVH.cpp FImplicitBVH::Serialize: the leaf objects and an empty hierarchy
    void Union(Dictionary<string, object> o)
    {
        o["Objects"] = c.Array(Pointer, 4);
        o["LocalBoundingBox"] = AABB();
        if (external < 29 || fortnite < 91) throw new FormatException("Chaos union with a legacy hierarchy");
        byte flags = r.ReadByte();
        o["Flags"] = flags;
        o["NumLeafObjects"] = season < 12 ? r.ReadUInt16() : r.ReadInt32();
        if ((flags & 2) == 0) return;
        o["BVHObjects"] = c.Array(() => new object[] { Pointer(), c.Floats(3), c.Floats(4), r.ReadInt32() }, 36);
        o["BVH"] = Hierarchy();
    }

    // BoundingVolumeHierarchy.cpp TBoundingVolumeHierarchy::Serialize: global objects, world-space boxes by object, maximum levels, the nodes
    // (BoundingVolumeHierarchy.h TBVHNode: leaf index, axis, children, maximum and minimum) and the leaves
    object Hierarchy() => new Dictionary<string, object> { ["GlobalObjects"] = c.Array(() => r.ReadInt32(), 4), ["WorldSpaceBoxes"] = c.Array(() => new object[] { r.ReadInt32(), AABB() }, 28),
        ["MaxLevels"] = r.ReadInt32(),
        ["Elements"] = c.Array(() => new object[] { r.ReadInt32(), r.ReadInt32(), c.Array(() => r.ReadInt32(), 4), c.Floats(3), c.Floats(3) }, 36),
        ["Leafs"] = c.Array(() => c.Array(() => r.ReadInt32(), 4), 4) };

    // Convex.h FConvex::SerializeImp, single precision: planes, vertices, bounds, volume and centre of mass, margin, half-edge
    // structure data (ConvexHalfEdgeStructureData.h, indices of the size its index type names), unit mass inertia and rotation of mass
    void Convex(Dictionary<string, object> o)
    {
        if (external < 31) throw new FormatException("Chaos convex saved before ConvexUsesTPlaneConcrete");
        o["Planes"] = Floats(24, 6);
        if (physics < 9 && main < 26 && fortnite < 46) throw new FormatException("Chaos convex with particle vertices");
        o["Vertices"] = Floats(12, 3);
        if (external < 25) throw new FormatException("Chaos convex bounds saved before TBoxReplacedWithTAABB");
        o["LocalBoundingBox"] = c.Floats(6);
        if (external >= 20) { o["Volume"] = r.ReadSingle(); o["CenterOfMass"] = c.Floats(3); }
        if (release >= 37) o["Margin"] = r.ReadSingle();
        if (release >= 38)
        {
            if (physics < 13) throw new FormatException("Chaos convex with legacy structure data");
            sbyte index = (sbyte)r.ReadByte();
            o["IndexType"] = index;
            int width = index switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, _ => throw new FormatException($"Invalid convex index type {index}") };
            if (width > 0)
            {
                object Indices(int per) { int n = c.Count(per * width); var v = new int[n * per]; for (int i = 0; i < v.Length; i++) v[i] = width == 1 ? r.ReadByte() : width == 2 ? r.ReadInt16() : r.ReadInt32(); return v; }
                o["StructurePlanes"] = Indices(2); o["StructureHalfEdges"] = Indices(3); o["StructureVertices"] = Indices(1);
                if (physics >= 14) o["StructureEdges"] = Indices(1);
            }
        }
        if (ue5release >= 34) { o["UnitMassInertiaTensor"] = c.Floats(3); o["RotationOfMass"] = c.Vector4(); }
    }

    float[] Floats(int size, int per) { int n = c.Count(size); return c.Floats(n * per); }
}
