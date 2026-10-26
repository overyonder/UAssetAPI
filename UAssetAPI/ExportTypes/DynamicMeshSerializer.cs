using System;
using System.Collections.Generic;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// GeometryCore DynamicMesh3_Serialization.cpp FDynamicMesh3::Serialize and the attribute set, overlay, attribute, ref count
/// vector, small list set and dynamic vector serializers it calls. Before DynamicMeshCompactedSerialization a mesh is its
/// containers as they were (dynamic vectors in 512-element blocks); after it, its options and its unique data, each dynamic
/// vector optionally compressed.
/// </summary>
internal static class DynamicMeshSerializer
{
    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    static readonly Guid FortniteReleaseGuid = UAPUtils.GUID(0xE7086368, 0x6B234C58, 0x84391B70, 0x16265E91);
    // FUE5MainStreamObjectVersion; FFortniteMainBranchObjectVersion; FFortniteReleaseBranchCustomObjectVersion
    const int DynamicMeshCompactedSerialization = 56, DynamicMeshAttributesWeightMapsAndNames = 62;
    const int DynamicMeshSerializeSculptLayers = 181, DynamicMeshAttributesMorphTargets = 224, DynamicMeshAttributesTriangleLabels = 257;
    const int DynamicMeshAttributesSerializeBones = 15;

    sealed class Reader
    {
        public ClassSerializers.Cursor C;
        public AssetBinaryReader R;
        public bool Legacy, Lwc;
        public int UE5, Fortnite, FortniteRelease;

        /// <summary>
        /// DynamicVector.h TDynamicVector::Serialize: legacy, the current block, its used count and the block count, then every
        /// block of 512 whole; now the count, whether the elements are compressed, then the elements. Element layouts: f float,
        /// d double, i int32, H uint16; a legacy double vector saved before large world coordinates holds floats.
        /// </summary>
        public Array Vector(string layout)
        {
            int width = layout.Length;
            char kind = layout[0];
            if (Legacy)
            {
                uint block = R.ReadUInt32(), used = R.ReadUInt32();
                int blocks = C.Count(1);
                if (kind == 'd' && !Lwc) kind = 'f';
                long total = (long)blocks * 512 * width, valid = (long)block * 512 * width + (long)used * width;
                if (valid > total) throw new FormatException($"Dynamic vector uses {valid} of {total} values");
                var all = Values(kind, total);
                return Slice(all, valid);
            }
            uint num = R.ReadUInt32();
            if (num == 0) return Values(kind, 0);
            bool compressed = C.Bool();
            long count = (long)num * width;
            if (!compressed) return Values(kind, count);
            var bytes = C.CompressedNew(count * Size(kind), "Oodle");
            using var sub = new AssetBinaryReader(new System.IO.MemoryStream(bytes), (UAsset)R.Asset);
            var saved = R; R = sub;
            try { return Values(kind, count); } finally { R = saved; }
        }

        static int Size(char kind) => kind switch { 'd' => 8, 'H' => 2, _ => 4 };
        static Array Slice(Array a, long n) { var o = Array.CreateInstance(a.GetType().GetElementType(), n); Array.Copy(a, o, n); return o; }

        Array Values(char kind, long count)
        {
            if (count * Size(kind) > R.BaseStream.Length - R.BaseStream.Position) throw new FormatException($"Dynamic vector of {count} values overruns the data");
            switch (kind)
            {
                case 'f': { var v = new float[count]; for (long i = 0; i < count; i++) v[i] = R.ReadSingle(); return v; }
                case 'd': { var v = new double[count]; for (long i = 0; i < count; i++) v[i] = R.ReadDouble(); return v; }
                case 'i': { var v = new int[count]; for (long i = 0; i < count; i++) v[i] = R.ReadInt32(); return v; }
                case 'H': { var v = new ushort[count]; for (long i = 0; i < count; i++) v[i] = R.ReadUInt16(); return v; }
                default: throw new ArgumentException(kind.ToString());
            }
        }

        public object Optional(string layout) => C.Bool() ? Vector(layout) : null;

        /// <summary>RefCountVector.h FRefCountVector::Serialize: legacy, the ref counts, free indices and used count; now whether
        /// compacted and compressed, the used count and the ref counts, then the free indices only when some are free and
        /// neither option was set.</summary>
        public Dictionary<string, object> RefCounts()
        {
            var d = new Dictionary<string, object>();
            if (Legacy) { d["RefCounts"] = Vector("H"); d["FreeIndices"] = Vector("i"); d["UsedCount"] = R.ReadInt32(); return d; }
            bool compact = C.Bool(), compress = C.Bool();
            int used = R.ReadInt32();
            var counts = Vector("H");
            d["UsedCount"] = used; d["RefCounts"] = counts;
            if (used != counts.Length && !compact && !compress) d["FreeIndices"] = Vector("i");
            return d;
        }

        /// <summary>SmallListSet.cpp FSmallListSet::Serialize: legacy, its containers; compacted or compressed, one buffer of the
        /// list count then each list's count and values; otherwise its containers.</summary>
        public Dictionary<string, object> SmallListSet()
        {
            if (Legacy) return new() { ["ListHeads"] = Vector("i"), ["ListBlocks"] = Vector("i"), ["FreeBlocks"] = Vector("i"),
                ["AllocatedCount"] = R.ReadInt32(), ["LinkedListElements"] = Vector("i"), ["FreeHeadIndex"] = R.ReadInt32() };
            bool compact = C.Bool(), compress = C.Bool();
            if (compact || compress) return new() { ["Lists"] = Vector("i") };
            return new() { ["ListHeads"] = Vector("i"), ["ListBlocks"] = Vector("i"), ["FreeBlocks"] = Vector("i"),
                ["LinkedListElements"] = Vector("i"), ["AllocatedCount"] = R.ReadInt32(), ["FreeHeadIndex"] = R.ReadInt32() };
        }

        /// <summary>DynamicMeshOverlay.cpp TDynamicMeshOverlay::Serialize: element ref counts, elements, parent vertices and
        /// element triangles.</summary>
        public Dictionary<string, object> Overlay() => new()
        {
            ["ElementsRefCounts"] = RefCounts(), ["Elements"] = Vector("f"), ["ParentVertices"] = Vector("i"), ["ElementTriangles"] = Vector("i"),
        };

        /// <summary>DynamicMeshTriangleAttribute.h and DynamicVertexAttribute.h: the name (DynamicAttribute.h, from
        /// DynamicMeshAttributesWeightMapsAndNames), then the values, preceded now by whether they are compressed.</summary>
        public Dictionary<string, object> Attribute(string layout)
        {
            var d = new Dictionary<string, object>();
            if (UE5 >= DynamicMeshAttributesWeightMapsAndNames) d["Name"] = C.Str();
            if (!Legacy) d["bUseCompression"] = C.Bool();
            d["Values"] = Vector(layout);
            return d;
        }

        List<T> Layers<T>(Func<T> read) => C.Array(read, 4);

        /// <summary>DynamicVertexSkinWeightsAttribute.h Serialize: whether compressed; uncompressed, each vertex's FBoneWeights
        /// (BoneWeights.h: bone index and raw weight pairs); compressed, a buffer of int32 holding the vertex count, then per
        /// vertex its weight count and each weight packed as index and raw weight.</summary>
        object SkinWeights()
        {
            bool compress = C.Bool();
            var vertices = new List<ushort[]>();
            if (!compress)
            {
                uint num = R.ReadUInt32();
                if (num == 0) return vertices;
                if (C.Bool()) throw new FormatException("Per-element bone weights flagged compressed");
                for (uint v = 0; v < num; v++)
                {
                    int n = C.Count(4);
                    var w = new ushort[n * 2];
                    for (int i = 0; i < n * 2; i++) w[i] = R.ReadUInt16();
                    vertices.Add(w);
                }
                return vertices;
            }
            int size = C.Count(4);
            var bytes = C.CompressedNew((long)size * 4, "Oodle");
            int at = 0;
            int Next() { if (at + 4 > bytes.Length) throw new FormatException("Skin weight buffer ends early"); int x = BitConverter.ToInt32(bytes, at); at += 4; return x; }
            int count = Next();
            for (int v = 0; v < count; v++)
            {
                int n = Next();
                var w = new ushort[n * 2];
                for (int i = 0; i < n; i++) { int x = Next(); w[i * 2] = (ushort)x; w[i * 2 + 1] = (ushort)(x >> 16); }
                vertices.Add(w);
            }
            if (at != bytes.Length) throw new FormatException($"Skin weight buffer read {at} of {bytes.Length} bytes");
            return vertices;
        }

        /// <summary>DynamicMeshAttributeSet.cpp SerializeLabels: the name count; when any, the unique names, then unless one name
        /// or all distinct, the index width and each triangle's index.</summary>
        object Labels()
        {
            var d = new Dictionary<string, object>();
            uint names = R.ReadUInt32();
            d["NumNames"] = names;
            if (names == 0) return d;
            int unique = C.Count(4);
            var list = new List<string>(unique);
            for (int i = 0; i < unique; i++) list.Add(C.Str());
            d["UniqueNames"] = list;
            if (unique == 1 || unique == names) return d;
            byte width = R.ReadByte();
            var idx = new uint[names];
            for (uint i = 0; i < names; i++) idx[i] = width switch { 0 => R.ReadByte(), 1 => R.ReadUInt16(), 2 => R.ReadUInt32(), _ => throw new FormatException($"Invalid label index size {width}") };
            d["Indices"] = idx;
            return d;
        }

        /// <summary>DynamicMeshAttributeSet.cpp FDynamicMeshAttributeSet::Serialize.</summary>
        public Dictionary<string, object> AttributeSet()
        {
            var d = new Dictionary<string, object>();
            if (Legacy)
            {
                d["UVLayers"] = Layers(Overlay); d["NormalLayers"] = Layers(Overlay); d["PolygroupLayers"] = Layers(() => Attribute("i"));
            }
            else
            {
                d["bUseCompression"] = C.Bool();
                d["UVLayers"] = Layers(Overlay); d["NormalLayers"] = Layers(Overlay); d["PolygroupLayers"] = Layers(() => Attribute("i"));
                if (UE5 >= DynamicMeshAttributesWeightMapsAndNames) d["WeightLayers"] = Layers(() => Attribute("f"));
            }
            int colors = R.ReadInt32();
            if (colors < 0 || colors > 1) throw new FormatException($"Invalid colour layer count {colors}");
            if (colors > 0) d["ColorLayer"] = Overlay();
            if (C.Bool()) d["MaterialID"] = Attribute("i");
            object Named(Func<object> read) => C.Array(() => { string key = C.Str(); return new object[] { key, C.Bool() ? read() : null }; }, 8);
            if (!Legacy) d["SkinWeightAttributes"] = Named(SkinWeights);
            if (Fortnite >= DynamicMeshAttributesMorphTargets) d["MorphTargetAttributes"] = Named(() => Attribute("f"));
            if (FortniteRelease >= DynamicMeshAttributesSerializeBones && C.Bool())
            {
                // DynamicBoneAttribute.cpp: each bone attribute's values; the names through FNameAsStringProxyArchive
                d["BoneNames"] = C.Array(C.Str, 4);
                d["BoneParentIndices"] = C.Array(() => R.ReadInt32(), 4);
                d["BonePoses"] = C.Array(() => new object[] { C.Doubles(4, Lwc), C.Vector(), C.Vector() }, 40);
                d["BoneColors"] = C.Array(() => C.Floats(4), 16);
            }
            if (Fortnite >= DynamicMeshSerializeSculptLayers)
            {
                int layers = R.ReadInt32();
                if (layers < 0) throw new FormatException($"Invalid sculpt layer count {layers}");
                if (layers > 0)
                {
                    d["SculptActiveLayer"] = R.ReadInt32();
                    d["SculptLayerWeights"] = C.Array(() => R.ReadDouble(), 8);
                    var list = new List<object>(layers);
                    for (int i = 0; i < layers; i++) list.Add(Attribute("d"));
                    d["SculptLayers"] = list;
                }
            }
            if (Fortnite >= DynamicMeshAttributesTriangleLabels) d["TriangleLabelAttributes"] = Named(Labels);
            return d;
        }
    }

    public static Dictionary<string, object> Read(ClassSerializers.Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var x = new Reader { C = c, R = r, UE5 = asset.GetCustomVersion(UE5MainGuid), Fortnite = asset.GetCustomVersion(FortniteMainGuid),
            FortniteRelease = asset.GetCustomVersion(FortniteReleaseGuid), Lwc = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES };
        x.Legacy = x.UE5 < DynamicMeshCompactedSerialization;
        var d = new Dictionary<string, object>();
        if (x.Legacy)
        {
            d["SerializationVersion"] = r.ReadInt32();
            d["Vertices"] = x.Vector("ddd");
            d["VertexRefCounts"] = x.RefCounts();
            d["VertexNormals"] = x.Optional("fff"); d["VertexColors"] = x.Optional("fff"); d["VertexUVs"] = x.Optional("ff");
            d["VertexEdgeLists"] = x.SmallListSet();
            d["Triangles"] = x.Vector("iii");
            d["TriangleRefCounts"] = x.RefCounts();
            d["TriangleEdges"] = x.Vector("iii");
            d["TriangleGroups"] = x.Optional("i");
            d["GroupIDCounter"] = r.ReadInt32();
            d["Edges"] = x.Vector("iiii");
            d["EdgeRefCounts"] = x.RefCounts();
            if (c.Bool()) d["Attributes"] = x.AttributeSet();
            return d;
        }
        bool preserve = c.Bool(), compact = c.Bool(), compress = c.Bool();
        d["Options"] = new object[] { preserve, compact, compress };
        d["Vertices"] = x.Vector("ddd");
        d["VertexNormals"] = x.Optional("fff"); d["VertexColors"] = x.Optional("fff"); d["VertexUVs"] = x.Optional("ff");
        d["Triangles"] = x.Vector("iii");
        d["TriangleGroups"] = x.Optional("i");
        d["GroupIDCounter"] = r.ReadInt32();
        if (!compact)
        {
            // the Default variant: triangle ref counts when the triangles have holes, and with the layout preserved the edges
            if (c.Bool()) d["TriangleRefCounts"] = x.RefCounts();
            if (preserve)
            {
                d["Edges"] = x.Vector("iiii"); d["EdgeRefCounts"] = x.RefCounts(); d["TriangleEdges"] = x.Vector("iii"); d["VertexEdgeLists"] = x.SmallListSet();
            }
        }
        if (c.Bool()) d["Attributes"] = x.AttributeSet();
        return d;
    }
}
