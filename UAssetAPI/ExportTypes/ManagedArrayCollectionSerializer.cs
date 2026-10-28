using System;
using System.Collections.Generic;
using System.Linq;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// Chaos ManagedArrayCollection.cpp FManagedArrayCollection::Serialize: its version, the groups by name (each a version and
/// its size), then the attributes keyed by attribute and group name, each its version, array type (ManagedArrayTypes.h
/// EManagedArrayType, in ManagedArrayTypeValues.inl order), dependency group and persistence, then the array as
/// ManagedArray.h writes it: a version, then the elements, bulk serialized (element size, count, bytes) from BulkSerializeArrays
/// for the types TryBulkSerializeManagedArray lists, otherwise each element as its own operator writes it.
/// </summary>
internal static class ManagedArrayCollectionSerializer
{
    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid DestructionGuid = UAPUtils.GUID(0x174F1F0B, 0xB4C645A5, 0xB13F2EE8, 0xD0FB917D);
    const int BulkSerializeArrays = 8, ManagedArrayCollectionAlwaysSerializeValue = 80;

    static readonly string[] TypeNames =
    {
        "None", "Vector", "IntVector", "Vector2D", "LinearColor", "Int32", "Bool", "Transform", "String", "Float", "Quat", "BoneNode",
        "MeshSection", "Box", "IntArray", "Guid", "UInt8", "VectorArrayPointer", "VectorArrayUniquePointer", "FImplicitObject3Pointer",
        "FImplicitObject3UniquePointer", "FImplicitObject3SerializablePtr", "FBVHParticlesFloat3Pointer", "FBVHParticlesFloat3UniquePointer",
        "TPBDRigidParticleHandle3fPtr", "TPBDGeometryCollectionParticleHandle3fPtr", "TGeometryParticle3fUniquePtr",
        "FImplicitObject3ThreadSafeSharedPointer", "FImplicitObject3SharedPointer", "TPBDRigidClusteredParticleHandle3fPtr", "FConvexUniquePtr",
        "Vector2DArray", "Double", "IntVector4", "Vector3d", "IntVector2", "IntVector2Array", "Int32Array", "FloatArray", "Vector4f",
        "FVectorArray", "TPBDRigidParticle3fUniquePtr", "FImplicitObjectRefCountedPtr", "FConvexRefCountedPtr", "Transform3f",
        "IntVector3Array", "Vector4fArray", "PMatrix33d", "PMatrix33dArray", "FVector3fNestedArray", "UintVector2", "UObjectArray",
        "LinearCurve", "Name", "SoftObjectPath",
    };

    // the element layout of each bulk serialized type: f float, i int32, I uint32, g FGuid, b uint8 (a bool array's elements too)
    static readonly Dictionary<int, string> Bulk = new()
    {
        [1] = "fff", [2] = "iii", [3] = "ff", [5] = "i", [6] = "b", [9] = "f", [10] = "ffff", [15] = "g", [16] = "b", [33] = "iiii", [35] = "ii", [50] = "II",
    };

    public static Dictionary<string, object> Read(ClassSerializers.Cursor c, AssetBinaryReader r, UAsset asset, ChaosImplicitSerializer chaos)
    {
        int destruction = asset.GetCustomVersion(DestructionGuid), main = asset.GetCustomVersion(UE5MainGuid);
        var d = new Dictionary<string, object> { ["Version"] = r.ReadInt32() };
        d["Groups"] = c.Array(() => new object[] { c.Name(), r.ReadInt32(), r.ReadInt32() }, 16);
        string previous = null;
        d["Attributes"] = c.Array(() =>
        {
            var a = new Dictionary<string, object> { ["Name"] = c.Name(), ["Group"] = c.Name() };
            int version = r.ReadInt32(), type = r.ReadInt32();
            asset.Coverage.Report("discarded-value", $"Managed array attribute {a["Name"]} ({a["Group"]}): serialization version {version} is read but not retained in the decoded attribute.");
            if (type < 0 || type >= TypeNames.Length) throw new FormatException($"Invalid managed array type {type}");
            a["Type"] = TypeNames[type];
            if (version < 4) a["Scope"] = r.ReadInt32();
            bool persistent = true;
            if (version >= 2) { a["GroupIndexDependency"] = c.Name(); persistent = c.Bool(); a["bPersistent"] = persistent; }
            if (main >= ManagedArrayCollectionAlwaysSerializeValue || persistent)
            {
                long at = r.BaseStream.Position;
                try { a["ArrayVersion"] = r.ReadInt32(); a["Values"] = Values(type); }
                catch (FormatException e) { throw new FormatException($"{a["Name"]} ({TypeNames[type]}, {a["Group"]}) from {at}, after {previous}: {e.Message}", e); }
            }
            previous = $"{a["Name"]} ({a["Type"]})";
            return a;
        }, 40);
        return d;

        object Values(int type)
        {
            bool bulk = destruction >= BulkSerializeArrays;
            if (Bulk.TryGetValue(type, out var layout))
            {
                if (bulk)
                {
                    int size = r.ReadInt32(), n = c.Count(size > 0 ? size : 1);
                    int expected = layout.Sum(ch => ch == 'g' ? 16 : ch == 'b' ? 1 : 4);
                    if (size != expected) throw new FormatException($"Managed array of {TypeNames[type]} with {size}-byte elements");
                    return Elements(layout, n, type == 6 ? 1 : 0);
                }
                // before BulkSerializeArrays each element through its operator, a bool as four bytes
                return Elements(layout, c.Count(type == 6 ? 4 : 1), type == 6 ? 4 : 0);
            }
            object Each(Func<object> read, int min) => c.Array(read, min);
            object Ints() => c.Array(() => r.ReadInt32(), 4);
            switch (type)
            {
                case 4: return Each(() => c.Floats(4), 16);
                case 7: return Each(() => new object[] { c.Vector4(), c.Vector(), c.Vector() }, 40);
                case 8: return Each(() => c.Str(), 4);
                case 11: return Each(() => new object[] { r.ReadInt32(), r.ReadInt32(), Ints(), r.ReadInt32() }, 16);
                case 12: return Each(() => new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32() }, 20);
                case 13: return Each(c.Box, 25);
                case 14: case 37: return Each(Ints, 4);
                case 31: return Each(() => { int n = c.Count(8); return c.Floats(n * 2); }, 4);
                case 32: return Each(() => r.ReadDouble(), 8);
                case 34: return Each(c.Vector, 12);
                case 36: return Each(() => { int n = c.Count(8); return Enumerable.Range(0, n * 2).Select(_ => r.ReadInt32()).ToArray(); }, 4);
                case 38: return Each(() => { int n = c.Count(4); return c.Floats(n); }, 4);
                case 39: return Each(() => c.Floats(4), 16);
                case 40: return Each(() => { int n = c.Count(12); return c.Floats(n * 3); }, 4);
                case 44: return Each(() => c.Floats(10), 40);
                case 45: return Each(() => { int n = c.Count(12); return Enumerable.Range(0, n * 3).Select(_ => r.ReadInt32()).ToArray(); }, 4);
                case 46: return Each(() => { int n = c.Count(16); return c.Floats(n * 4); }, 4);
                case 49: return Each(() => c.Array(() => { int n = c.Count(12); return c.Floats(n * 3); }, 4), 4);
                case 51: return Each(() => new FPackageIndex(r), 4);
                case 53: return Each(c.Name, 8);
                case 54: return Each(() => new FSoftObjectPath(r), 8);
                // TRefCountPtr, TUniquePtr and TSerializablePtr of implicit objects and convexes, through FChaosArchive
                case 20: case 21: case 27: case 28: case 30: case 42: case 43: return Each(chaos.Pointer, 4);
                case 23: return Each(chaos.BVHParticles, 4);
                default: throw new FormatException($"Managed array of Chaos type {TypeNames[type]}");
            }
        }

        object Elements(string layout, int n, int boolWidth)
        {
            var values = new List<object>(n);
            for (int i = 0; i < n; i++)
            {
                var v = new object[layout.Length];
                for (int k = 0; k < layout.Length; k++)
                    v[k] = layout[k] switch
                    {
                        'f' => r.ReadSingle(), 'i' => r.ReadInt32(), 'I' => r.ReadUInt32(), 'g' => r.ReadGuid(),
                        _ => boolWidth == 4 ? c.Bool() : boolWidth == 1 ? r.ReadByte() switch { 0 => false, 1 => true, var x => throw new FormatException($"Invalid bool {x}") } : (object)r.ReadByte(),
                    };
                values.Add(layout.Length == 1 ? v[0] : v);
            }
            return values;
        }
    }
}
