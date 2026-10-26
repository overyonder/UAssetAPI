using System;
using System.Collections.Generic;
using System.IO;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// The native Serialize overrides of further engine classes, each read after the tagged properties and the tails of its base
/// classes, as an editor (uncooked, unfiltered) package saves them. Each returns its fields by name; the bytes it consumed are
/// kept for writing. Data that is only written when cooking, and geometry, stays as the bytes it was.
/// </summary>
internal static class ClassSerializers
{
    static readonly Guid AnimGuid = UAPUtils.GUID(0xAF43A65D, 0x7FD34947, 0x98733E8E, 0xD9C1BB05);
    static readonly Guid AnimPhysGuid = UAPUtils.GUID(0x29E575DD, 0xE0A34627, 0x9D10D276, 0x232CDCEA);
    static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
    static readonly Guid RenderingGuid = UAPUtils.GUID(0x12F88B9F, 0x88754AFC, 0xA67CD90C, 0x383ABD29);
    static readonly Guid NiagaraGuid = UAPUtils.GUID(0xFCF57AFA, 0x50764283, 0xB9A9E658, 0xFFA02D32);
    static readonly Guid OptimusGuid = UAPUtils.GUID(0x93ede1aa, 0x10ca7375, 0x4df98a28, 0x49b157a0);
    static readonly Guid CurveExpressionGuid = UAPUtils.GUID(0xA26D36AE, 0x26935388, 0xA8C5CB96, 0x2B95B4AF);
    static readonly Guid SkeletalMeshGuid = UAPUtils.GUID(0xD78A4A00, 0xE8584697, 0xBAA819B5, 0x487D46B4);

    // ObjectVersion.h
    const int REFERENCE_SKELETON_REFACTOR = 310, FIX_ANIMATIONBASEPOSE_SERIALIZATION = 331, SKELETON_GUID_SERIALIZATION = 338,
        STORE_BONE_EXPORT_NAMES = 370, SKELETON_ADD_SMARTNAMES = 388, MOVE_SKELETALMESH_SHADOWCASTING = 302;
    // FAnimObjectVersion, FAnimPhysObjectVersion, FFrameworkObjectVersion, FUE5ReleaseStreamObjectVersion
    const int StoreMarkerNamesOnSkeleton = 2, ChangeRetargetSourceReferenceToSoftObjectPtr = 15, SmartNameRefactorForDeterministicCooking = 10,
        AddLODToCurveMetaData = 12, SmartNameRefactor = 7, MoveCurveTypesToSkeleton = 15, RemovedSmartNameContainerPayload = 64;
    // FNiagaraCustomVersion, FOptimusObjectVersion, FCurveExpressionCustomVersion, FFortniteMainBranchObjectVersion, FRenderingObjectVersion
    const int ChangeEmitterCompiledDataToSharedRefs = 51, NiagaraShaderMaps = 5, SwitchToMeshDeformerBase = 1, SerializedExpressions = 1,
        ExpressionDataInSharedObject = 2, RemoveAnimCurveCompressionCodecInstanceGuid = 119, AnimNextModuleRefactor = 168,
        MovedParticleCutoutsToRequiredModule = 8, TextureStreamingMeshUVChannelData = 10, MeshMaterialSlotOverlayMaterialAdded = 196;
    // FEditorObjectVersion, FCoreObjectVersion
    const int RefactorMeshEditorMaterials = 8, SkeletalMaterialEditorDataStripping = 3;

    internal static bool Handles(NativeClassTail tails) => (tails & All) != 0;

    internal const NativeClassTail All = NativeClassTail.Skeleton | NativeClassTail.PhysicsAsset | NativeClassTail.NiagaraSystem |
        NativeClassTail.NiagaraScript | NativeClassTail.ComputeGraph | NativeClassTail.OptimusDeformer | NativeClassTail.DNAAsset |
        NativeClassTail.CurveExpressionsDataAsset | NativeClassTail.AnimCurveCompressionCodec | NativeClassTail.ParticleModuleRequired |
        NativeClassTail.SkeletalMesh | NativeClassTail.UAFSystem;

    internal static Dictionary<string, object> Read(NativeClassTail tails, AssetBinaryReader r, UAsset asset)
    {
        var d = new Dictionary<string, object>();
        var c = new Cursor(r);
        long Remaining() => r.BaseStream.Length - r.BaseStream.Position;
        byte[] Rest() => r.ReadBytes(checked((int)Remaining()));

        if (tails.HasFlag(NativeClassTail.Skeleton)) ReadSkeleton(d, c, r, asset);
        if (tails.HasFlag(NativeClassTail.PhysicsAsset))
            // PhysicsAsset.cpp: CollisionDisableTable, TMap<FRigidBodyIndexPair, bool>
            d["CollisionDisableTable"] = c.Array(() => new object[] { r.ReadInt32(), r.ReadInt32(), c.Bool() }, 12);
        if (tails.HasFlag(NativeClassTail.NiagaraSystem) && asset.GetCustomVersion(NiagaraGuid) >= ChangeEmitterCompiledDataToSharedRefs)
            d["EmitterCompiledData"] = c.Array(() => c.Tagged("NiagaraEmitterCompiledData"), 8);
        if (tails.HasFlag(NativeClassTail.NiagaraScript) && asset.GetCustomVersion(NiagaraGuid) >= NiagaraShaderMaps && Remaining() > 0)
        {
            // NiagaraScript.cpp SerializeNiagaraShaderMaps: whether the count is present depends on the script's usage when saved,
            // and it is the export's last field, so it is present exactly when bytes remain; editor saves write no shader maps
            int n = r.ReadInt32();
            d["LoadedScriptResources"] = n;
            if (n != 0) d["ShaderMaps"] = Rest();
        }
        if (tails.HasFlag(NativeClassTail.ComputeGraph))
        {
            // ComputeGraph.cpp: the kernel count, then each FComputeKernelResource::SerializeShaderMap: a cooked flag, then the
            // cooked shader map, which only cooking writes
            int n = c.Count(4);
            var kernels = new List<object>(n);
            for (int i = 0; i < n; i++)
            {
                bool cooked = c.Bool();
                kernels.Add(cooked);
                if (cooked) { d["CookedShaderMaps"] = Rest(); break; }
            }
            d["KernelCooked"] = kernels;
        }
        if (tails.HasFlag(NativeClassTail.OptimusDeformer) && asset.GetCustomVersion(OptimusGuid) < SwitchToMeshDeformerBase)
            // OptimusDeformer.cpp: the kernel and resource counts UComputeGraph wrote before SwitchToMeshDeformerBase
            d["LegacyKernelResourceCounts"] = c.Array(() => r.ReadInt32(), 4);
        if (tails.HasFlag(NativeClassTail.DNAAsset))
            // DNAAsset.cpp, DNA.cpp: the DNA file read straight from the archive by FArchiveMemoryStream (two before the custom
            // version, behavior then geometry); it is the rest of the export
            d["DNA"] = Rest();
        if (tails.HasFlag(NativeClassTail.CurveExpressionsDataAsset))
        {
            if (asset.GetCustomVersion(CurveExpressionGuid) >= ExpressionDataInSharedObject) d["NamedConstants"] = c.Array(c.Name, 8);
            d["ExpressionMap"] = c.Array(() => new object[] { c.Name(), c.Expression() }, 12);
        }
        if (tails.HasFlag(NativeClassTail.AnimCurveCompressionCodec) && asset.GetCustomVersion(FortniteMainGuid) < RemoveAnimCurveCompressionCodecInstanceGuid)
            d["LegacyInstanceGuid"] = r.ReadGuid();
        if (tails.HasFlag(NativeClassTail.ParticleModuleRequired) && asset.GetCustomVersion(RenderingGuid) >= MovedParticleCutoutsToRequiredModule)
        {
            bool cooked = c.Bool();
            d["bCooked"] = cooked;
            if (cooked) d["DerivedData"] = Rest();
        }
        if (tails.HasFlag(NativeClassTail.SkeletalMesh)) ReadSkeletalMesh(d, c, r, asset);
        if (tails.HasFlag(NativeClassTail.UAFSystem) && asset.GetCustomVersion(FortniteMainGuid) < AnimNextModuleRefactor)
            // AnimNextModule.cpp: an older module's shared archive buffer, which the engine skips by seeking to the export's end
            d["SkippedLegacyBuffer"] = Rest();
        return d;
    }

    /// <summary>Animation/Skeleton.cpp USkeleton::Serialize.</summary>
    static void ReadSkeleton(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int ue4 = (int)asset.ObjectVersion;
        if (ue4 >= REFERENCE_SKELETON_REFACTOR) d["ReferenceSkeleton"] = ReadReferenceSkeleton(c, r, asset);
        if (ue4 >= FIX_ANIMATIONBASEPOSE_SERIALIZATION)
            // SerializeReferencePose: the pose name and bone transforms, then the editor-only source mesh
            d["AnimRetargetSources"] = c.Array(() => new object[] { c.Name(), c.Name(), c.Array(c.Transform, 40),
                asset.GetCustomVersion(AnimPhysGuid) < ChangeRetargetSourceReferenceToSoftObjectPtr ? new FPackageIndex(r) : new FSoftObjectPath(r) }, 16);
        if (ue4 >= SKELETON_GUID_SERIALIZATION) d["Guid"] = r.ReadGuid();
        if (ue4 >= SKELETON_ADD_SMARTNAMES && (int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() < RemovedSmartNameContainerPayload)
            // SmartName.cpp FSmartNameContainer::Serialize: the name mappings, TMap<FName, FSmartNameMapping>
            d["SmartNames_DEPRECATED"] = c.Array(() => new object[] { c.Name(), ReadSmartNameMapping(c, r, asset) }, 8);
        if (asset.GetCustomVersion(AnimGuid) >= StoreMarkerNamesOnSkeleton)
        {
            var strip = r.ReadBytes(2);
            d["StripFlags"] = strip;
            if ((strip[0] & 1) == 0) d["ExistingMarkerNames"] = c.Array(c.Name, 8);
        }
    }

    static object ReadSmartNameMapping(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var m = new Dictionary<string, object>();
        int framework = (int)asset.GetCustomVersion<FFrameworkObjectVersion>();
        if (framework >= SmartNameRefactor)
        {
            if (asset.GetCustomVersion(AnimPhysGuid) < SmartNameRefactorForDeterministicCooking)
                m["GuidMap"] = c.Array(() => new object[] { c.Name(), r.ReadGuid() }, 24);
        }
        else if ((int)asset.ObjectVersion >= SKELETON_ADD_SMARTNAMES)
        {
            m["NextUid"] = r.ReadUInt16();
            m["UidMap"] = c.Array(() => new object[] { r.ReadUInt16(), c.Name() }, 10);
        }
        if (framework >= MoveCurveTypesToSkeleton)
            // AnimCurveMetaData.cpp: material and morph target flags, linked bones (FBoneReference, its name), and the maximum LOD
            m["CurveMetaDataMap"] = c.Array(() => new object[] { c.Name(), c.Bool(), c.Bool(), c.Array(c.Name, 8),
                asset.GetCustomVersion(AnimPhysGuid) >= AddLODToCurveMetaData ? r.ReadByte() : null }, 20);
        return m;
    }

    /// <summary>ReferenceSkeleton.cpp: the bone infos (name, parent, editor export name), the bone pose and the name to index map.</summary>
    static object ReadReferenceSkeleton(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int ue4 = (int)asset.ObjectVersion;
        var bones = c.Array(() =>
        {
            var name = c.Name(); int parent = r.ReadInt32();
            if (ue4 < REFERENCE_SKELETON_REFACTOR) r.ReadUInt32();
            string export = ue4 >= STORE_BONE_EXPORT_NAMES && !asset.IsFilterEditorOnly ? c.Str() : null;
            return new object[] { name, parent, export };
        }, 12);
        var pose = c.Array(c.Transform, 40);
        object map = ue4 >= REFERENCE_SKELETON_REFACTOR ? c.Array(() => new object[] { c.Name(), r.ReadInt32() }, 12) : null;
        return new object[] { bones, pose, map };
    }

    /// <summary>
    /// SkeletalMesh.cpp USkeletalMesh::Serialize up to the reference skeleton: strip flags, imported bounds, materials and the
    /// reference skeleton. The rest is the imported model and render data, geometry, kept as bytes.
    /// </summary>
    static void ReadSkeletalMesh(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        d["StripFlags"] = r.ReadBytes(2);
        d["ImportedBounds"] = r.ReadBytes(asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES ? 7 * 8 : 7 * 4);
        if ((int)asset.GetCustomVersion<FEditorObjectVersion>() < RefactorMeshEditorMaterials)
            throw new FormatException("FSkeletalMaterial saved before FEditorObjectVersion::RefactorMeshEditorMaterials");
        // SkinnedAssetCommon.cpp operator<<(FArchive&, FSkeletalMaterial&)
        d["Materials"] = c.Array(() =>
        {
            var m = new List<object> { new FPackageIndex(r), c.Name() };
            bool imported = true;
            if ((int)asset.GetCustomVersion<FCoreObjectVersion>() >= SkeletalMaterialEditorDataStripping) { imported = c.Bool(); m.Add(imported); }
            if (imported) m.Add(c.Name());
            if (asset.GetCustomVersion(RenderingGuid) >= TextureStreamingMeshUVChannelData)
                m.Add(new object[] { c.Bool(), c.Bool(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() });
            if (asset.GetCustomVersion(FortniteMainGuid) >= MeshMaterialSlotOverlayMaterialAdded) m.Add(new FPackageIndex(r));
            return m;
        }, 12);
        d["ReferenceSkeleton"] = ReadReferenceSkeleton(c, r, asset);
        d["ModelAndRenderData"] = r.ReadBytes(checked((int)(r.BaseStream.Length - r.BaseStream.Position)));
    }

    internal sealed class Cursor
    {
        readonly AssetBinaryReader r;
        public Cursor(AssetBinaryReader reader) { r = reader; }

        public int Count(int minBytes)
        {
            int n = r.ReadInt32();
            if (n < 0 || (long)n * minBytes > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Invalid count {n} at {r.BaseStream.Position - 4}");
            return n;
        }
        public List<T> Array<T>(Func<T> read, int minBytes) { int n = Count(minBytes); var l = new List<T>(n); for (int i = 0; i < n; i++) l.Add(read()); return l; }
        public string Str() => r.ReadFString()?.Value;
        public object Name() => r.ReadFName().ToString();
        public bool Bool() { uint v = r.ReadUInt32(); if (v > 1) throw new FormatException($"Invalid bool {v}"); return v == 1; }
        /// <summary>An FTransform by operator&lt;&lt;: rotation, translation and scale, double precision with large world coordinates.</summary>
        public object Transform()
        {
            bool lwc = r.Asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;
            var v = new double[10];
            for (int i = 0; i < 10; i++) v[i] = lwc ? r.ReadDouble() : r.ReadSingle();
            return v;
        }
        /// <summary>A struct's tagged properties, as UScriptStruct::SerializeTaggedProperties writes them.</summary>
        public StructPropertyData Tagged(string structType)
        {
            var s = new StructPropertyData(FName.DefineDummy(r.Asset, structType), FName.DefineDummy(r.Asset, structType));
            s.Read(r, false, 1, 0, PropertySerializationContext.StructFallback);
            return s;
        }
        /// <summary>
        /// CurveExpression ExpressionEvaluator.cpp FExpressionObject::Load: the operand count, then each operand's variant index
        /// (operator, constant name, function index, value) and value.
        /// </summary>
        public object Expression() => Array<object>(() =>
        {
            int type = r.ReadInt32();
            return type switch
            {
                0 => new object[] { "Operator", r.ReadInt32() },
                1 => new object[] { "Constant", Name() },
                2 => new object[] { "Function", r.ReadInt32() },
                3 => new object[] { "Value", r.ReadSingle() },
                _ => throw new FormatException($"Invalid curve expression element type {type}"),
            };
        }, 8);
    }
}
