using System.Linq;
using Newtonsoft.Json;
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

    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid UE5ReleaseGuid = UAPUtils.GUID(0xD89B5E42, 0x24BD4D46, 0x8412ACA8, 0xDF641779);
    static readonly Guid EditorGuid = UAPUtils.GUID(0xE4B068ED, 0xF49442E9, 0xA231DA0B, 0x2E46BB41);
    static readonly Guid FrameworkGuid = UAPUtils.GUID(0xCFFC743F, 0x43B04480, 0x939114DF, 0x171D2073);
    static readonly Guid CoreGuid = UAPUtils.GUID(0x375EC13C, 0x06E448FB, 0xB50084F0, 0x262A717E);
    static readonly Guid ReleaseGuid = UAPUtils.GUID(0x9C54D522, 0xA8264FBE, 0x94210746, 0x61B482D0);
    static readonly Guid MobileGuid = UAPUtils.GUID(0xB02B49B5, 0xBB2044E9, 0xA30432B7, 0x52E40360);
    static readonly Guid PhysicsGuid = UAPUtils.GUID(0x78F01B33, 0xEBEA4F98, 0xB9B484EA, 0xCCB95AA2);
    static readonly Guid DestructionGuid = UAPUtils.GUID(0x174F1F0B, 0xB4C645A5, 0xB13F2EE8, 0xD0FB917D);
    static readonly Guid LandscapeGuid = UAPUtils.GUID(0x23AFE18E, 0x4CE14E58, 0x8D61C252, 0xB953BEB7);
    static readonly Guid FoliageGuid = UAPUtils.GUID(0x430C4D19, 0x71544970, 0x87699B69, 0xDF90B0E5);
    static readonly Guid EdgeSnapshotGuid = UAPUtils.GUID(0x12345678, 0x12345678, 0x12345678, 0x12345678);
    static readonly Guid SequencerGuid = UAPUtils.GUID(0x7B5AE74C, 0xD2704C10, 0xA9585798, 0x0B212A5A);
    static readonly Guid MediaGuid = UAPUtils.GUID(0x6F0ED827, 0xA6094895, 0x9C91998D, 0x90180EA4);
    static readonly Guid InterchangeGuid = UAPUtils.GUID(0x92738C43, 0x29884D9C, 0x9A3D9BBE, 0x6EFF9FC0);
    static readonly Guid VariantManagerGuid = UAPUtils.GUID(0x24BB7AF3, 0x56464F83, 0x1F2F2DC2, 0x49AD96FF);
    static readonly Guid MLDeformerGuid = UAPUtils.GUID(0x23D9FD27, 0x4F6A0FDF, 0xEA055480, 0x7D4B3AB0);
    static readonly Guid PCGGuid = UAPUtils.GUID(0x2763920D, 0x0F784B39, 0x986E4BB3, 0xA88D666D);
    static readonly Guid ReflectionCaptureGuid = UAPUtils.GUID(0x6B266CEC, 0x1EC74B8F, 0xA30BE4D9, 0x0942FC07);
    static readonly Guid NNEModelDataGuid = UAPUtils.GUID(0x9513202E, 0xEBA1B279, 0xF17FE5BA, 0xAB90C3F2);
    static readonly Guid LegacyReflectionCaptureVer = UAPUtils.GUID(0x0C669396, 0x9CB849AE, 0x9F4120FF, 0x5812F4D3);

    /// <summary>
    /// Reads the tails of <paramref name="chain"/>, the classes with a case below that the export's class is or derives from,
    /// base first. <paramref name="fileOffset"/> is where the reader's stream starts in the package file.
    /// </summary>
    internal static Dictionary<string, object> Read(string[] chain, AssetBinaryReader r, UAsset asset, NormalExport export, long fileOffset)
    {
        var d = new Dictionary<string, object>();
        var c = new Cursor(r);
        long Remaining() => r.BaseStream.Length - r.BaseStream.Position;
        byte[] Rest() => r.ReadBytes(checked((int)Remaining()));
        int Ver(Guid g) => asset.GetCustomVersion(g);
        bool Lwc = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;

        foreach (string cls in chain)
            switch (cls)
            {
                case "USkeleton": ReadSkeleton(d, c, r, asset); break;
                case "UPhysicsAsset":
                    // PhysicsAsset.cpp: CollisionDisableTable, TMap<FRigidBodyIndexPair, bool>
                    d["CollisionDisableTable"] = c.Array(() => new object[] { r.ReadInt32(), r.ReadInt32(), c.Bool() }, 12);
                    break;
                case "UNiagaraSystem":
                    if (Ver(NiagaraGuid) >= ChangeEmitterCompiledDataToSharedRefs) d["EmitterCompiledData"] = c.Array(() => c.Tagged("NiagaraEmitterCompiledData"), 8);
                    break;
                case "UNiagaraScript":
                    // NiagaraScript.cpp SerializeNiagaraShaderMaps: whether the count is present depends on the script's usage when
                    // saved, and it is the export's last field, so it is present exactly when bytes remain; editor saves write no
                    // shader maps
                    if (Ver(NiagaraGuid) >= NiagaraShaderMaps && Remaining() > 0)
                    {
                        int n = r.ReadInt32();
                        d["LoadedScriptResources"] = n;
                        if (n != 0) d["ShaderMaps"] = Rest();
                    }
                    break;
                case "UComputeGraph":
                {
                    // ComputeGraph.cpp: the kernel count, then each FComputeKernelResource::SerializeShaderMap: a cooked flag, then
                    // the cooked shader map, which only cooking writes
                    int n = c.Count(4);
                    var kernels = new List<object>(n);
                    for (int i = 0; i < n; i++)
                    {
                        bool cooked = c.Bool();
                        kernels.Add(cooked);
                        if (cooked) { d["CookedShaderMaps"] = new Payload("cooked shader maps", Rest()); break; }
                    }
                    d["KernelCooked"] = kernels;
                    break;
                }
                case "UOptimusDeformer":
                    // OptimusDeformer.cpp: the kernel and resource counts UComputeGraph wrote before SwitchToMeshDeformerBase
                    if (Ver(OptimusGuid) < SwitchToMeshDeformerBase) d["LegacyKernelResourceCounts"] = c.Array(() => r.ReadInt32(), 4);
                    break;
                case "UDNAAsset":
                case "UDNA":
                    // DNAAsset.cpp, DNA.cpp: the DNA file read straight from the archive by FArchiveMemoryStream (two before the
                    // custom version, behavior then geometry); it is the rest of the export
                    d["DNA"] = new Payload("RigLogic DNA", Rest());
                    break;
                case "UCurveExpressionsDataAsset":
                    if (Ver(CurveExpressionGuid) >= ExpressionDataInSharedObject) d["NamedConstants"] = c.Array(c.Name, 8);
                    d["ExpressionMap"] = c.Array(() => new object[] { c.Name(), c.Expression() }, 12);
                    break;
                case "UAnimCurveCompressionCodec":
                    if (Ver(FortniteMainGuid) < RemoveAnimCurveCompressionCodecInstanceGuid) d["LegacyInstanceGuid"] = r.ReadGuid();
                    break;
                case "UParticleModuleRequired":
                    if (Ver(RenderingGuid) >= MovedParticleCutoutsToRequiredModule) Cooked(d, c, Rest);
                    break;
                case "USkeletalMesh": ReadSkeletalMesh(d, c, r, asset, export); break;
                case "UUAFSystem":
                    // AnimNextModule.cpp: an older module's shared archive buffer, which the engine skips by seeking to the export's end
                    if (Ver(FortniteMainGuid) < AnimNextModuleRefactor) d["SkippedLegacyBuffer"] = Rest();
                    break;

                case "UTexture":
                {
                    // Texture.cpp: strip flags, then with editor data the source's bulk data, an FEditorBulkData; before
                    // FUE5MainStreamObjectVersion::TextureSourceVirtualization a legacy FByteBulkData
                    var strip = c.StripFlags(d);
                    if ((strip[0] & 1) == 0) d["Source"] = Ver(UE5MainGuid) < 42 ? c.ByteBulk("texture source") : c.EditorBulk();
                    break;
                }
                case "UTexture2D":
                {
                    // Texture2D.cpp: strip flags and the cooked flag; cooked, whether mips follow and the platform data
                    c.StripFlags(d, "Texture2DStripFlags");
                    bool cooked = c.Bool();
                    d["bCooked"] = cooked;
                    if (cooked) { d["bSerializeMipData"] = c.Bool(); d["CookedPlatformData"] = new Payload("cooked texture platform data", Rest()); }
                    break;
                }
                case "UTexture2DArray":
                case "UTextureCube":
                case "UTextureCubeArray":
                case "UVolumeTexture":
                    // their Serialize overrides: strip flags and the cooked flag, then cooked platform data
                    c.StripFlags(d, cls + "StripFlags");
                    Cooked(d, c, Rest);
                    break;
                case "ULightMapTexture2D":
                    // LightMap.cpp: the lightmap flags
                    d["LightmapFlags"] = r.ReadUInt32();
                    break;
                case "USoundWave": ReadSoundWave(d, c, r, asset, Rest); break;
                case "USoundWaveProcedural": break;
                case "UPointCloudImpl":
                {
                    // CitySample RuleProcessor PointCloudImpl.cpp: from MantleDbSerialize whether the database follows, then
                    // DeSerializeDb: its size, the SQLite database compressed (SerializeCompressedNew), and its SHA-1 digest
                    if (Ver(UE5MainGuid) < MantleDbSerialize) break;
                    bool has = c.Bool();
                    d["bHasData"] = has;
                    if (!has) break;
                    long size = r.ReadInt64();
                    d["DatabaseSize"] = size;
                    d["Database"] = new Payload("SQLite database", c.CompressedNew(size, "Zlib"));
                    d["WholeDbHash"] = Convert.ToHexString(r.ReadBytes(20));
                    break;
                }
                case "UMetaHumanCharacter":
                {
                    // MetaHumanCharacter MetaHumanCharacter.cpp: the face and body state and DNA bulk data, the synthesized face
                    // textures (one per EFaceTextureType), the high-resolution body textures (one per EBodyTextureType, from
                    // BodyTexturesSerialized), the posed body target states by target mesh key (from
                    // BodyTargetPoseStatesSerialized) and the face model coefficients at each target pose (from
                    // FaceModelCoefficientsAtTargetPoseSerialized); a target mesh key is its body, head and combined meshes
                    int v = Ver(MetaHumanCharacterGuid);
                    d["FaceState"] = c.EditorBulk(); d["FaceDNA"] = c.EditorBulk(); d["BodyState"] = c.EditorBulk(); d["BodyDNA"] = c.EditorBulk();
                    d["SynthesizedFaceTextures"] = c.Fixed(9, c.EditorBulk);
                    if (v >= 1) d["HighResBodyTextures"] = c.Fixed(11, c.EditorBulk);
                    object Key() => new object[] { new FSoftObjectPath(r), new FSoftObjectPath(r), new FSoftObjectPath(r) };
                    if (v >= 2) d["BodyTargetPoseStates"] = c.Array(() => new object[] { Key(), c.EditorBulk() }, 48);
                    if (v >= 3) d["FaceModelCoefficientsAtTargetPose"] = c.Array(() => new object[] { Key(), c.Array(() => r.ReadSingle(), 4) }, 16);
                    break;
                }
                case "USoundClass":
                    // SoundClass.cpp: before SOUND_CLASS_GRAPH_EDITOR the editor node positions by sound class
                    if (asset.ObjectVersion < ObjectVersion.VER_UE4_SOUND_CLASS_GRAPH_EDITOR)
                        d["EditorData_DEPRECATED"] = c.Array(() => new object[] { new FPackageIndex(r), r.ReadInt32(), r.ReadInt32() }, 12);
                    break;
                case "UFont":
                    // Font.cpp: CharRemap, TMap<uint16, uint16>
                    d["CharRemap"] = c.Array(() => new object[] { r.ReadUInt16(), r.ReadUInt16() }, 4);
                    break;
                case "UFontFace":
                {
                    // FontFace.cpp: the cooked flag from AddedCookedBoolFontFaceAssets; from AddedInlineFontFaceAssets whether the
                    // face data is inline, then FFontFaceData::Serialize: the font file and, from AddedPreprocessedFontGeometry,
                    // its preprocessed glyph geometry, the export's last field
                    if (Ver(UE5MainGuid) >= 12) d["bCooked"] = c.Bool();
                    if (Ver(EditorGuid) >= 14)
                    {
                        bool inline = c.Bool();
                        d["bInline"] = inline;
                        if (inline)
                        {
                            d["FontData"] = new Payload("font file", c.Bytes());
                            // FFontFaceData::Serialize: TArray<FPreprocessedFontGeometry>, each GlobalWindingReversal, the TMap<int32, FGlyphHeader>
                            // Glyphs (Flags uint8, five int32), ContourData TArray<uint8> and CoordinateData TArray<int16> (PreprocessedFontGeometry.cpp)
                            if (Ver(FortniteMainGuid) >= 180) d["PreprocessedFontGeometries"] = c.Array(() => new Dictionary<string, object>
                            {
                                ["GlobalWindingReversal"] = c.Bool(),
                                ["Glyphs"] = c.Array(() => new object[] { r.ReadInt32(), r.ReadByte(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32() }, 25),
                                ["ContourData"] = c.Bytes(),
                                ["CoordinateData"] = c.Array(() => r.ReadInt16(), 2),
                            }, 16);
                        }
                    }
                    break;
                }
                case "UVectorFieldStatic":
                    // VectorField.cpp: SourceData, an FByteBulkData
                    d["SourceData"] = c.ByteBulk("vector field volume");
                    break;
                case "USubUVAnimation":
                    Cooked(d, c, Rest);
                    break;
                case "UCurveTable": ReadCurveTable(d, c, r, asset); break;
                case "UNNEModelData": ReadNNEModelData(d, c, r, asset); break;
                case "UInterchangeAssetImportData":
                    // InterchangeAssetImportData.cpp: from SerializedInterchangeObjectStoring the node container (a TArray64<uint8>
                    // of its serialized form) and the pipelines (class path and JSON pairs)
                    if (Ver(InterchangeGuid) >= 1)
                    {
                        // InterchangeBaseNodeContainer.cpp SerializeNodeContainerData, written with the package's custom versions:
                        // the node count, then each node's class full name and its UInterchangeBaseNode::Serialize, its attributes
                        byte[] container = c.Bytes64();
                        if (container.Length > 0)
                        {
                            using var nr = new AssetBinaryReader(new MemoryStream(container), asset);
                            var nc = new Cursor(nr);
                            d["CachedNodeContainer"] = nc.Array(() => new object[] { nc.Str(), ReadAttributeStorage(nc, nr, asset) }, 8);
                            if (nr.BaseStream.Position != container.Length) throw new FormatException($"Node container read {nr.BaseStream.Position} of {container.Length} bytes");
                        }
                        else d["CachedNodeContainer"] = null;
                        d["CachedPipelines"] = c.Array(() => new object[] { c.Str(), c.Str() }, 8);
                    }
                    break;
                case "UInterchangeBaseNode": d["Attributes"] = ReadAttributeStorage(c, r, asset); break;
                case "UBaseMediaSource": ReadBaseMediaSource(d, c, r, asset); break;
                case "UMeshDescriptionBaseBulkData": d["BulkData"] = ReadMeshDescriptionBulkData(c, r, asset); break;
                case "USkeletalMeshEditorData":
                    // SkeletalMeshEditorData.cpp, SkeletalMeshLODImporterData.cpp: the raw imported LODs, each its import versions
                    // (from SkeletalMeshBuildRefactor), an FByteBulkData, its GUID and whether that GUID is a hash
                    d["RawSkeletalMeshBulkDatas"] = c.Array<object>(() =>
                    {
                        var e = new List<object>();
                        if (Ver(EditorGuid) >= 37) { e.Add(r.ReadByte()); e.Add(r.ReadByte()); }
                        e.Add(c.ByteBulk()); e.Add(r.ReadGuid()); e.Add(c.Bool());
                        return e;
                    }, 21);
                    break;
                case "UMorphTarget":
                {
                    // MorphTarget.cpp: strip flags; without audio-visual data stripped, the cooked flag from
                    // MorphTargetCookedCPUDataCompressed, then the LOD models, geometry and the export's last field when not cooked
                    var strip = c.StripFlags(d);
                    if ((strip[0] & 2) != 0) break;
                    bool cooked = Ver(FortniteMainGuid) >= 211 && c.Bool();
                    d["bCooked"] = cooked;
                    d["MorphLODModels"] = c.Array<object>(() => ReadMorphTargetLODModel(c, r, asset), 4);
                    // a cooked package then stores the GPU-compressed deltas, which editor packages never hold
                    if (cooked) d["CookedCompressedLODModels"] = new Payload("cooked compressed morph deltas", Rest());
                    break;
                }
                case "UMLDeformerMorphModel":
                    // MLDeformerMorphModel.cpp: per LOD (one before LODSupportAdded) whether compressed morph buffers follow, and
                    // the FMorphTargetVertexInfoBuffers
                    if (Ver(MLDeformerGuid) < 1) d["MorphBuffers"] = new List<object> { MorphBuffers(c, r) };
                    else d["MorphBuffers"] = c.Array(() => MorphBuffers(c, r), 4);
                    break;
                case "UDynamicMesh":
                    // UDynamicMesh.cpp: the FDynamicMesh3, geometry and the export's last field
                    d["Mesh"] = DynamicMeshSerializer.Read(c, r, asset);
                    break;
                case "UMeshPartitionCollisionComponent":
                {
                    // MeshPartitionCollisionComponent.cpp: with editor data, whether collision data follows, whether it has a mesh,
                    // and the mesh as PhysicsCore.cpp writes an FTriMeshCollisionData: vertices, triangle indices, material indices,
                    // UV channels, the vertices again, its flags, and from OptionalQuantizePerFaceBoundsInChaosTriMeshBVH one more
                    bool has = c.Bool();
                    d["bHasCollisionData"] = has;
                    if (has && c.Bool())
                    {
                        float[] Vertices() { int n = c.Count(12); return c.Floats(n * 3); }
                        var mesh = new Dictionary<string, object> { ["Vertices"] = Vertices() };
                        { int n = c.Count(12); var t = new int[n * 3]; for (int i = 0; i < t.Length; i++) t[i] = r.ReadInt32(); mesh["Indices"] = t; }
                        { int n = c.Count(2); var m = new ushort[n]; for (int i = 0; i < n; i++) m[i] = r.ReadUInt16(); mesh["MaterialIndices"] = m; }
                        mesh["UVs"] = c.Array(() => { int n = c.Count(c.Lwc ? 16 : 8); return c.Doubles(n * 2, c.Lwc); }, 4);
                        mesh["VerticesAgain"] = Vertices();
                        mesh["bFlipNormals"] = c.Bool(); mesh["bDeformableMesh"] = c.Bool(); mesh["bFastCook"] = c.Bool(); mesh["bDisableActiveEdgePrecompute"] = c.Bool();
                        if (Ver(FortniteMainGuid) >= 235) mesh["bReducedMemoryRepresentation"] = c.Bool();
                        d["Mesh"] = mesh;
                    }
                    break;
                }
                case "UGroomAsset": ReadGroomAsset(d, c, r, asset, export, Rest); break;
                case "UGeometryCache":
                    // GeometryCache.cpp: from GeometryCacheAssetDeprecation only the tagged properties
                    if (Ver(AnimPhysGuid) < 17) throw new FormatException("Geometry cache saved before GeometryCacheAssetDeprecation");
                    break;
                case "UGeometryCacheTrack":
                    // GeometryCacheTrack.cpp: the matrix samples, their times and the material count
                    d["MatrixSamples"] = c.Array(() => c.Doubles(16, Lwc), Lwc ? 128 : 64);
                    d["MatrixSampleTimes"] = c.Array(() => r.ReadSingle(), 4);
                    d["NumMaterials"] = r.ReadUInt32();
                    break;
                case "UGeometryCacheTrackStreamable":
                    // GeometryCacheTrackStreamable.cpp: the chunks (bulk data, size, first and last frame), the samples (time,
                    // bounding box, vertex and index counts) and the visibility samples (time range and state)
                    d["Chunks"] = c.Array(() => new object[] { c.ByteBulk("geometry cache chunk"), r.ReadInt32(), r.ReadSingle(), r.ReadSingle() }, 24);
                    d["Samples"] = c.Array(() => new object[] { r.ReadSingle(), c.Box(), r.ReadInt32(), r.ReadInt32() }, 20);
                    d["VisibilitySamples"] = c.Array(() => new object[] { r.ReadByte(), r.ReadSingle(), r.ReadByte(), r.ReadSingle(), c.Bool() }, 14);
                    break;
                case "UNiagaraSimCache":
                    // NiagaraSimCache.cpp: from SimCache_BulkDataVersion1 each frame's system and emitter buffers, the rest of the export
                    if (Ver(NiagaraGuid) >= 84) d["CacheFrameBuffers"] = new Payload("Niagara simulation cache frames", Rest());
                    break;
                case "UPCGLandscapeCache":
                    // PCGLandscapeCache.cpp: the cache entries, each its landscape GUID and coordinate, then
                    // FPCGLandscapeCacheEntry::Serialize: point half size, stride, layer names and the bulk data
                    d["CachedData"] = c.Array(() => new object[] { r.ReadGuid(), r.ReadInt32(), r.ReadInt32(), c.Vector(), r.ReadInt32(),
                        c.Array(c.Name, 8), c.ByteBulk("PCG landscape cache samples") }, 40);
                    break;
                case "UPCGMetadata":
                    // PCGMetadata.cpp: from MultiLevelMetadata the default domain and the domain IDs, then the domains' attributes;
                    // before it the default domain's attributes. The attributes are generated output, the rest of the export.
                    if (Ver(PCGGuid) >= 24)
                    {
                        d["DefaultDomain"] = new object[] { r.ReadByte(), r.ReadInt32() };
                        var ids = c.Array(() => new object[] { r.ReadByte(), r.ReadInt32() }, 5);
                        d["DomainIDs"] = ids;
                        // per domain, whether it is valid, then (unless it is the default domain) its attributes
                        d["Domains"] = ids.Select(id =>
                        {
                            bool valid = c.Bool();
                            return (object)new object[] { id, valid, valid && (byte)id[0] != 0 ? ReadPCGMetadataDomain(c, r, asset) : null };
                        }).ToList();
                    }
                    else d["Domains"] = new List<object> { ReadPCGMetadataDomain(c, r, asset) };
                    break;
                case "UDataflow": d["Graph"] = ReadDataflowGraph(c, r, asset); break;
                case "UPCGPolygon2DData":
                    // PCG Data/PCGPolygon2DData.cpp: the polygon (outer vertices, then each hole's), the transform and the
                    // vertex metadata entry keys
                    d["OuterVertices"] = c.Array(() => c.Doubles(2, true), 16);
                    d["Holes"] = c.Array(() => c.Array(() => c.Doubles(2, true), 16), 4);
                    d["Transform"] = c.Transform();
                    d["VertexEntryKeys"] = c.Array(() => r.ReadInt64(), 8);
                    break;
                case "UDataflowEdNode": d["DataflowNodeGuid"] = r.ReadGuid(); break;
                // AssetImportData.cpp, FbxSceneImportData.cpp: their JSON precedes the tagged properties, read by AssetImportDataExport
                case "UAssetImportData":
                case "UFbxSceneImportData": break;
                case "UVariantSet":
                case "UVariant":
                    // VariantSet.cpp, Variant.cpp: DisplayText from CategoryFlagsAndManualDisplayText
                    if (Ver(VariantManagerGuid) >= 2) d[cls + "DisplayText"] = c.Text();
                    break;
                case "UPropertyValue":
                {
                    // PropertyValue.cpp: the leaf property class from FCoreObjectVersion::FProperties, the object, and the
                    // recorded name, string and text (only the name from CorrectSerializationOfFNameBytes)
                    if (Ver(CoreGuid) >= 4) d["LeafPropertyClass"] = c.Name();
                    d["TempObjPtr"] = new FSoftObjectPath(r);
                    int v = Ver(VariantManagerGuid);
                    if (v >= 3) { d["TempName"] = c.Name(); d["TempStr"] = c.Str(); d["TempText"] = c.Text(); }
                    else if (v >= 1) d["TempName"] = c.Name();
                    break;
                }
                case "UFieldSystem":
                    // FieldSystemAsset.cpp, Field/FieldSystem.cpp: from FieldsAdded the commands, each its target attribute, field
                    // type, serialization type and field node tree
                    if (Ver(DestructionGuid) >= 6) d["Commands"] = c.Array(() => new object[] { c.Name(), FieldNode(c, r, r.ReadByte(), r.ReadByte(), 0) }, 3);
                    break;
                case "UGeometryCollection": ReadGeometryCollection(d, c, r, asset, Rest); break;
                case "UAtmosphericFogComponent":
                    // AtmosphericFogComponent.cpp: before RemovedAtmosphericFog, the precomputed transmittance and irradiance
                    // (from ATMOSPHERIC_FOG_CACHE_DATA) and inscatter bulk data, and a counter
                    if (Ver(UE5MainGuid) < 46)
                    {
                        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_ATMOSPHERIC_FOG_CACHE_DATA) { d["Transmittance"] = c.ByteBulk("atmospheric fog lookup table"); d["Irradiance"] = c.ByteBulk("atmospheric fog lookup table"); }
                        d["Inscatter"] = c.ByteBulk("atmospheric fog lookup table");
                        d["Counter"] = r.ReadInt32();
                    }
                    break;
                case "UReflectionCaptureComponent": ReadReflectionCaptureLegacy(d, c, r, asset, fileOffset); break;
                case "UInstancedStaticMeshComponent": ReadInstancedStaticMesh(d, c, r, asset, Rest); break;
                case "UHierarchicalInstancedStaticMeshComponent":
                    // HierarchicalInstancedStaticMesh.cpp: the cluster tree, bulk serialized
                    // FClusterNode: bound minimum, first child, bound maximum, last child, first and last instance, minimum and
                    // maximum instance scale
                    d["ClusterTree"] = c.Bulk("fffifffiiiffffff");
                    break;
                case "UInstancedSkinnedMeshComponent": ReadInstancedSkinnedMesh(d, c, r, asset, Rest); break;
                case "AInstancedFoliageActor": ReadInstancedFoliageActor(d, c, r, asset); break;
                // LevelInstanceActorGuid.cpp: FLevelInstanceActorGuid is written only when cooking or duplicating for PIE
                case "ALevelInstance": break;
                case "APackedLevelActor":
                    if (Ver(UE5ReleaseGuid) >= 9) d["PackedVersion"] = r.ReadGuid();
                    break;
                case "UNavCollision": ReadNavCollision(d, c, r, asset, export); break;
                case "ARecastNavMesh":
                case "URecastNavMeshDataChunk":
                {
                    // RecastNavMesh.cpp, RecastNavMeshDataChunk.cpp: the nav mesh version and the size of the recast data that
                    // follows (uint32 for the actor, int64 for a chunk), counted from the size's own position
                    d["NavMeshVersion"] = r.ReadInt32();
                    long sizePos = r.BaseStream.Position;
                    long size = cls == "ARecastNavMesh" ? r.ReadUInt32() : r.ReadInt64();
                    if (size < (cls == "ARecastNavMesh" ? 4 : 8) || sizePos + size > r.BaseStream.Length) throw new FormatException($"Invalid recast data size {size}");
                    d["RecastData"] = new Payload("Recast navmesh tiles", r.ReadBytes((int)(sizePos + size - r.BaseStream.Position)));
                    break;
                }
                case "UModel": ReadModel(d, c, r, asset); break;
                case "UPolys":
                    // Polygon.cpp: the polygons
                    if (asset.ObjectVersion < ObjectVersion.VER_UE4_BSP_UNDO_FIX) throw new FormatException("Polys saved before BSP_UNDO_FIX");
                    d["Element"] = c.Array(() => Poly(c, r, asset), 100);
                    break;
                case "UModelComponent":
                    // ModelComponent.cpp: the model, its elements (map build data GUID from FixedBSPLightmaps, component,
                    // material, nodes), the component index and the nodes
                    if (Ver(RenderingGuid) < 9) throw new FormatException("Model component saved before MapBuildDataSeparatePackage");
                    d["Model"] = new FPackageIndex(r);
                    d["Elements"] = c.Array(() => new object[] { Ver(RenderingGuid) >= 12 ? r.ReadGuid() : null, new FPackageIndex(r),
                        new FPackageIndex(r), c.Array(() => r.ReadUInt16(), 2) }, 12);
                    d["ComponentIndex"] = r.ReadInt32();
                    d["Nodes"] = c.Array(() => r.ReadUInt16(), 2);
                    break;
                case "UMapBuildDataRegistry":
                    if ((c.StripFlags(d)[0] & 2) == 0) ReadMapBuildData(d, c, r, asset);
                    break;
                case "ULandscapeComponent": ReadLandscapeComponent(d, c, r, asset, export); break;
                case "ULandscapeHeightfieldCollisionComponent":
                    // LandscapeCollision.cpp: before LANDSCAPE_COLLISION_DATA_COOKING the height and dominant layer bulk data;
                    // after it the cooked flag, then cooked collision data or, with editor data, those two and, from
                    // LandscapePhysicalMaterialRenderData, the physical material render data
                    if (asset.ObjectVersion < ObjectVersion.VER_UE4_LANDSCAPE_COLLISION_DATA_COOKING) { d["CollisionHeightData"] = c.ByteBulk("landscape collision heights"); d["DominantLayerData"] = c.ByteBulk("landscape dominant layers"); break; }
                    {
                        bool cooked = c.Bool();
                        d["bCooked"] = cooked;
                        if (cooked) { d["CookedCollisionData"] = c.Bulk(); break; }
                        d["CollisionHeightData"] = c.ByteBulk("landscape collision heights"); d["DominantLayerData"] = c.ByteBulk("landscape dominant layers");
                        if (Ver(FortniteMainGuid) >= 37) d["PhysicalMaterialRenderData"] = c.ByteBulk("landscape physical material render data");
                    }
                    break;
                case "ULandscapeHeightmapTextureEdgeFixup":
                {
                    // LandscapeEdgeFixup.cpp operator<<(FHeightmapTextureEdgeSnapshot): edge length, edge data, legacy corner
                    // data, snapshot hashes, the texture source ID and the initial hashes, by the snapshot's own version
                    int v = Ver(EdgeSnapshotGuid);
                    d["EdgeLength"] = r.ReadInt32();
                    d["EdgeData"] = c.Array(() => r.ReadUInt32(), 4);
                    if (v <= 2) d["CornerData"] = c.Fixed(4, () => r.ReadUInt32());
                    d["SnapshotEdgeHashes"] = c.Fixed(8, () => r.ReadUInt32());
                    if (v > 0) d["TextureSourceID"] = r.ReadGuid();
                    if (v > 1) d["InitialEdgeHashes"] = c.Fixed(8, () => r.ReadUInt32());
                    break;
                }
                case "ULandscapeTextureMipEdgeOverrideFactory": d["Texture"] = new FPackageIndex(r); break;
                case "ULandscapeSplinesComponent":
                    // LandscapeSplines.cpp: with editor data, before NewSplineCrossLevelMeshSerialization the foreign world spline
                    // data by world, each its segment data map (the segment's lazy pointer GUID, then modification key and mesh
                    // components)
                    if (asset.ObjectVersion >= ObjectVersion.VER_UE4_LANDSCAPE_SPLINE_CROSS_LEVEL_MESHES && Ver(LandscapeGuid) < 1)
                        d["ForeignWorldSplineDataMap"] = c.Array(() => new object[] { new FSoftObjectPath(r),
                            c.Array(() => new object[] { r.ReadGuid(), r.ReadGuid(), c.Array(() => new FPackageIndex(r), 4) }, 36) }, 12);
                    break;
                default: throw new InvalidOperationException($"No class serializer for {cls}");
            }
        return d;
    }

    static void Cooked(Dictionary<string, object> d, Cursor c, Func<byte[]> rest)
    {
        bool cooked = c.Bool();
        d["bCooked"] = cooked;
        if (cooked) d["CookedData"] = rest();
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
            var strip = c.StripFlags(d);
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
            if (ue4 < REFERENCE_SKELETON_REFACTOR)
            {
                asset.Coverage.Report("discarded-value", "Legacy reference-skeleton bone metadata was read without retaining it.");
                r.ReadUInt32();
            }
            string export = ue4 >= STORE_BONE_EXPORT_NAMES && !asset.IsFilterEditorOnly ? c.Str() : null;
            return new object[] { name, parent, export };
        }, 12);
        var pose = c.Array(c.Transform, 40);
        object map = ue4 >= REFERENCE_SKELETON_REFACTOR ? c.Array(() => new object[] { c.Name(), r.ReadInt32() }, 12) : null;
        return new object[] { bones, pose, map };
    }

    /// <summary>
    /// SkeletalMesh.cpp USkeletalMesh::Serialize: strip flags, imported bounds, materials, the reference skeleton, the imported
    /// model (unless editor data is stripped), the cooked flag, the legacy object array, and the per-poly collision body setup.
    /// </summary>
    static void ReadSkeletalMesh(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, NormalExport export)
    {
        var strip = c.StripFlags(d);
        // FBoxSphereBounds: origin, box extent and sphere radius
        var bounds = c.Doubles(7, c.Lwc);
        d["ImportedBounds"] = new Dictionary<string, object> { ["Origin"] = bounds[..3], ["BoxExtent"] = bounds[3..6], ["SphereRadius"] = bounds[6] };
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
        int skel = asset.GetCustomVersion(SkeletalMeshGuid);
        if (skel < SplitModelAndRenderData) throw new FormatException("skeletal mesh saved before FSkeletalMeshCustomVersion::SplitModelAndRenderData");
        if ((strip[0] & 1) == 0) d["ImportedModel"] = ReadSkeletalMeshModel(c, r, asset);
        bool cooked = c.Bool();
        d["bCooked"] = cooked;
        // FSkeletalMeshRenderData follows only in cooked packages
        if (cooked) d["CookedRenderData"] = new Payload("cooked skeletal mesh render data", r.ReadBytes(checked((int)(r.BaseStream.Length - r.BaseStream.Position))));
        if (asset.ObjectVersion < ObjectVersion.VER_UE4_REFERENCE_SKELETON_REFACTOR) throw new FormatException("skeletal mesh saved before VER_UE4_REFERENCE_SKELETON_REFACTOR");
        d["DummyObjs"] = c.Array(() => new FPackageIndex(r), 4);
        if (asset.GetCustomVersion(RenderingGuid) < TextureStreamingMeshUVChannelData) d["CachedStreamingTextureFactors"] = c.Array(() => r.ReadSingle(), 4);
        if ((strip[0] & 1) == 0 && skel <= RemoveSourceData && c.Bool()) d["SourceData"] = ReadSkeletalMeshLODModel(c, r, asset);
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_APEX_CLOTH && skel < NewClothingSystemAdded)
            throw new FormatException("skeletal mesh saved before FSkeletalMeshCustomVersion::NewClothingSystemAdded");
        // bEnablePerPolyCollision is a tagged property; the body setup reference follows when it is set
        if (export?.Data?.Find(p => p.Name?.ToString() == "bEnablePerPolyCollision") is PropertyTypes.Objects.BoolPropertyData { Value: true })
            d["BodySetup"] = new FPackageIndex(r);
    }

    // FSkeletalMeshCustomVersion, FRecomputeTangentCustomVersion, FOverlappingVerticesCustomVersion; UE5 main stream versions
    const int CombineSectionWithChunk = 1, CombineSoftAndRigidVerts = 2, SaveNumVertices = 4, NewClothingSystemAdded = 8,
        RemoveSourceData = 11, SplitModelAndRenderData = 12, RemoveTriangleSorting = 13, RemoveDuplicatedClothingSections = 14,
        DeprecateSectionDisabledFlag = 15, SectionIgnoreByReduceAdded = 16, SkinWeightProfiles = 17, RemoveEnableClothLOD = 18,
        RuntimeRecomputeTangent = 1, RecomputeTangentVertexColorMask = 2, DetectOverlappingVertices = 1,
        IncreaseNormalPrecision = 26, IncreaseBoneIndexLimitPerChunk = 4, UnlimitedBoneInfluences = 5,
        SkelMeshSectionVisibleInRayTracingFlagAdded = 54, InlineReductionCacheData = 57, SkeletalMeshLODModelMeshInfo = 58,
        IncreasedSkinWeightPrecision = 90;
    static readonly Guid RecomputeTangentGuid = UAPUtils.GUID(0x5579F886, 0x933A4C1F, 0x83BA087B, 0x6361B92F);
    static readonly Guid OverlappingVerticesGuid = UAPUtils.GUID(0x612FBE52, 0xDA53400B, 0x910D4F91, 0x9FB1857C);

    /// <summary>SkeletalMeshModel.cpp FSkeletalMeshModel::Serialize: strip flags, the LOD models, the model GUID and whether it
    /// is a hash, and (with editor data) the inline reduction cache, each LOD's cached vertex and triangle counts.</summary>
    static Dictionary<string, object> ReadSkeletalMeshModel(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var m = new Dictionary<string, object>();
        int fortnite = (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>();
        bool editorStripped = false;
        if (fortnite >= (int)FFortniteMainBranchObjectVersion.AllowSkeletalMeshToReduceTheBaseLOD) editorStripped = (c.StripFlags(m)[0] & 1) != 0;
        m["LODModels"] = c.Array<object>(() => ReadSkeletalMeshLODModel(c, r, asset), 8);
        m["SkeletalMeshModelGUID"] = r.ReadGuid();
        m["bGuidIsHash"] = c.Bool();
        if (!editorStripped)
        {
            // FReductionBaseSkeletalMeshBulkData::Serialize: per LOD, the reduction source model in byte bulk data
            if (fortnite >= (int)FFortniteMainBranchObjectVersion.AllowSkeletalMeshToReduceTheBaseLOD && asset.GetCustomVersion(UE5MainGuid) < InlineReductionCacheData)
                m["OriginalReductionSourceMeshData"] = c.Array<object>(() => ReductionSource(c.ByteBulk(), asset), 20);
            if (asset.GetCustomVersion(UE5MainGuid) >= InlineReductionCacheData)
                m["InlineReductionCacheDatas"] = c.Array(() => new object[] { r.ReadUInt32(), r.ReadUInt32() }, 8);
        }
        return m;
    }

    /// <summary>SkeletalMeshLODModel.cpp FSkeletalMeshLODModel::Serialize for packages from SplitModelAndRenderData: strip flags,
    /// sections, section user data, the index buffer, active bones, imported mesh infos, the legacy size, vertex count, required
    /// bones, raw point indices, the raw source data ID, the import vertex map, texture coordinate count and skin weight profiles.</summary>
    // SkeletalMeshLODImporterData.cpp operator<<(FReductionSkeletalMeshData): written by FBulkDataWriter with the package's
    // versions, it holds its own version pair, the base LOD model and, unless the archive already ends, the morph deltas by name
    static Dictionary<string, object> ReductionSource(Dictionary<string, object> bulk, UAsset asset)
    {
        if (bulk["Payload"] is not byte[] bytes) return bulk;
        using var r = new AssetBinaryReader(new MemoryStream(bytes), asset);
        var c = new Cursor(r);
        var d = new Dictionary<string, object> { ["Version"] = r.ReadInt32(), ["LicenseeVersion"] = r.ReadInt32(),
            ["BaseLODModel"] = ReadSkeletalMeshLODModel(c, r, asset) };
        if (r.BaseStream.Position < bytes.Length)
            d["BaseLODMorphTargetData"] = c.Array(() => new object[] { c.Str(), c.Array(() => new object[] { c.Floats(3), c.Floats(3), r.ReadUInt32() }, 28) }, 8);
        if (r.BaseStream.Position != bytes.Length) throw new FormatException($"Reduction source read {r.BaseStream.Position} of {bytes.Length} bytes");
        bulk["Payload"] = d;
        return bulk;
    }

    static Dictionary<string, object> ReadSkeletalMeshLODModel(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var m = new Dictionary<string, object>();
        var strip = c.StripFlags(m);
        bool editor = (strip[0] & 1) == 0, audioVisual = (strip[0] & 2) == 0;
        int editorVer = (int)asset.GetCustomVersion<FEditorObjectVersion>(), main = asset.GetCustomVersion(UE5MainGuid);
        int skel = asset.GetCustomVersion(SkeletalMeshGuid), release = (int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>();
        int fortnite = (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>();
        m["Sections"] = c.Array<object>(() => ReadSkelMeshSection(c, r, asset), 8);
        if ((editor || !audioVisual) && editorVer >= (int)FEditorObjectVersion.SkeletalMeshBuildRefactor)
            m["UserSectionsData"] = c.Array(() => new object[] { r.ReadInt32(), ReadSkelMeshSourceSectionUserData(c, r, asset) }, 4);
        if (editor || !audioVisual) m["IndexBuffer"] = c.Array(() => r.ReadUInt32(), 4);
        m["ActiveBoneIndices"] = c.Array(() => r.ReadUInt16(), 4);
        if ((editor || !audioVisual) && main >= SkeletalMeshLODModelMeshInfo)
            m["ImportedMeshInfos"] = c.Array(() => new object[] { c.Name(), r.ReadInt32(), r.ReadInt32() }, 16);
        if (skel < CombineSectionWithChunk) throw new FormatException("LOD model saved before FSkeletalMeshCustomVersion::CombineSectionWithChunk");
        m["LegacySize"] = r.ReadUInt32();
        if (audioVisual) m["NumVertices"] = r.ReadUInt32();
        m["RequiredBones"] = c.Array(() => r.ReadUInt16(), 2);
        if (editor)
        {
            if (release < (int)FUE5ReleaseStreamObjectVersion.RemoveSkeletalMeshLODModelBulkDatas) m["RawPointIndices"] = c.ByteBulk("uint32 raw point indices");
            else m["RawPointIndices"] = c.Array(() => r.ReadUInt32(), 4);
            if (fortnite >= (int)FFortniteMainBranchObjectVersion.NewSkeletalMeshImporterWorkflow && editorVer < (int)FEditorObjectVersion.SkeletalMeshMoveEditorSourceDataToPrivateAsset)
                throw new FormatException("FRawSkeletalMeshBulkData saved before SkeletalMeshMoveEditorSourceDataToPrivateAsset");
            if (editorVer >= (int)FEditorObjectVersion.SkeletalMeshMoveEditorSourceDataToPrivateAsset)
            {
                m["RawSkeletalMeshBulkDataID"] = c.Str();
                m["bIsBuildDataAvailable"] = c.Bool();
                m["bIsRawSkeletalMeshBulkDataEmpty"] = c.Bool();
            }
        }
        m["MeshToImportVertexMap"] = c.Array(() => r.ReadInt32(), 4);
        m["MaxImportVertex"] = r.ReadInt32();
        if (audioVisual) m["NumTexCoords"] = r.ReadUInt32();
        if (skel >= SkinWeightProfiles)
            m["SkinWeightProfiles"] = c.Array(() => new object[] { c.Name(), new object[] {
                c.Array(() => ReadRawSkinWeight(r, asset), 8),
                c.Array(() => new object[] { r.ReadSingle(), r.ReadInt32(), r.ReadInt32() }, 12) } }, 16);
        return m;
    }

    /// <summary>SkeletalMeshLODModel.cpp operator&lt;&lt;(FArchive&amp;, FSkelMeshSection&amp;) from CombineSectionWithChunk.</summary>
    static Dictionary<string, object> ReadSkelMeshSection(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var s = new Dictionary<string, object>();
        var strip = c.StripFlags(s);
        bool editor = (strip[0] & 1) == 0, audioVisual = (strip[0] & 2) == 0;
        int skel = asset.GetCustomVersion(SkeletalMeshGuid), tangent = asset.GetCustomVersion(RecomputeTangentGuid);
        int anim = asset.GetCustomVersion(AnimGuid), main = asset.GetCustomVersion(UE5MainGuid);
        s["MaterialIndex"] = r.ReadUInt16();
        if (audioVisual) { s["BaseIndex"] = r.ReadUInt32(); s["NumTriangles"] = r.ReadUInt32(); }
        if (skel < RemoveTriangleSorting) s["TriangleSorting_DEPRECATED"] = r.ReadByte();
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_APEX_CLOTH)
        {
            if (skel < DeprecateSectionDisabledFlag) s["bLegacyClothingSection_DEPRECATED"] = c.Bool();
            if (skel < RemoveDuplicatedClothingSections) s["CorrespondClothSectionIndex_DEPRECATED"] = r.ReadInt16();
        }
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_APEX_CLOTH_LOD && skel < RemoveEnableClothLOD) s["EnableClothLOD_DEPRECATED"] = r.ReadByte();
        if (tangent >= RuntimeRecomputeTangent) s["bRecomputeTangent"] = c.Bool();
        if (tangent >= RecomputeTangentVertexColorMask) s["RecomputeTangentsVertexMaskChannel"] = r.ReadByte();
        if ((int)asset.GetCustomVersion<FEditorObjectVersion>() >= RefactorMeshEditorMaterials) s["bCastShadow"] = c.Bool();
        if (main >= SkelMeshSectionVisibleInRayTracingFlagAdded) s["bVisibleInRayTracing"] = c.Bool();
        if (audioVisual) s["BaseVertexIndex"] = r.ReadUInt32();
        if (editor)
        {
            if (skel < CombineSoftAndRigidVerts) throw new FormatException("section saved before FSkeletalMeshCustomVersion::CombineSoftAndRigidVerts");
            s["SoftVertices"] = ReadSoftSkinVertices(c, r, asset);
        }
        // older sections had 8-bit bone indices only and didn't serialize the flag (FSkelMeshSection::CalcUse16BitBoneIndex)
        s["bUse16BitBoneIndex"] = anim < IncreaseBoneIndexLimitPerChunk ? false : c.Bool();
        s["BoneMap"] = c.Array(() => r.ReadUInt16(), 2);
        if (skel >= SaveNumVertices) s["NumVertices"] = r.ReadInt32();
        s["MaxBoneInfluences"] = r.ReadInt32();
        if ((int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>() < (int)FUE5ReleaseStreamObjectVersion.AddClothMappingLODBias)
            s["ClothMappingDataLODs"] = new object[] { c.Array(() => ReadMeshToMeshVertData(r, asset), 64) };
        else s["ClothMappingDataLODs"] = c.Array<object>(() => c.Array(() => ReadMeshToMeshVertData(r, asset), 64), 4);
        if (skel < RemoveDuplicatedClothingSections) throw new FormatException("section saved before FSkeletalMeshCustomVersion::RemoveDuplicatedClothingSections");
        s["CorrespondClothAssetIndex"] = r.ReadInt16();
        s["ClothingData"] = new object[] { r.ReadGuid(), r.ReadInt32() };
        if (asset.GetCustomVersion(OverlappingVerticesGuid) >= DetectOverlappingVertices)
            s["OverlappingVertices"] = c.Array(() => new object[] { r.ReadInt32(), c.Array(() => r.ReadInt32(), 4) }, 8);
        if ((int)asset.GetCustomVersion<FReleaseObjectVersion>() >= (int)FReleaseObjectVersion.AddSkeletalMeshSectionDisable) s["bDisabled"] = c.Bool();
        if (skel >= SectionIgnoreByReduceAdded) s["GenerateUpToLodIndex"] = r.ReadInt32();
        if ((int)asset.GetCustomVersion<FEditorObjectVersion>() >= (int)FEditorObjectVersion.SkeletalMeshBuildRefactor)
        {
            s["OriginalDataSectionIndex"] = r.ReadInt32();
            s["ChunkedParentSectionIndex"] = r.ReadInt32();
        }
        return s;
    }

    /// <summary>SkeletalMeshLODModel.cpp operator&lt;&lt;(FArchive&amp;, FSoftSkinVertex&amp;), as parallel arrays: positions, tangent
    /// X, Y and Z (Z with its handedness), four UV channels, colours (FColor, as stored), 12 bone indices and 12 weights.</summary>
    static Dictionary<string, object> ReadSoftSkinVertices(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int n = c.Count(40);
        bool packed = asset.GetCustomVersion(RenderingGuid) < IncreaseNormalPrecision;
        int anim = asset.GetCustomVersion(AnimGuid);
        bool wideWeights = asset.GetCustomVersion(UE5MainGuid) >= IncreasedSkinWeightPrecision;
        bool eight = asset.ObjectVersion >= ObjectVersion.VER_UE4_SUPPORT_8_BONE_INFLUENCES_SKELETAL_MESHES;
        int influences = anim >= UnlimitedBoneInfluences ? 12 : eight ? 8 : 4;
        var position = new float[n * 3]; var tx = new float[n * 3]; var ty = new float[n * 3]; var tz = new float[n * 4];
        var uv = new float[n * 8]; var color = new uint[n]; var bones = new ushort[n * 12]; var weights = new ushort[n * 12];
        void Packed(float[] a, int at, int width)
        {
            // FDeprecatedSerializedPackedNormal: four bytes, each mapped from [0, 255] to [-1, 1]
            for (int k = 0; k < 4; k++) { float v = r.ReadByte() / 127.5f - 1; if (k < width) a[at + k] = v; }
        }
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < 3; k++) position[i * 3 + k] = r.ReadSingle();
            if (packed) { Packed(tx, i * 3, 3); Packed(ty, i * 3, 3); Packed(tz, i * 4, 4); }
            else
            {
                for (int k = 0; k < 3; k++) tx[i * 3 + k] = r.ReadSingle();
                for (int k = 0; k < 3; k++) ty[i * 3 + k] = r.ReadSingle();
                for (int k = 0; k < 4; k++) tz[i * 4 + k] = r.ReadSingle();
            }
            for (int k = 0; k < 8; k++) uv[i * 8 + k] = r.ReadSingle();
            color[i] = r.ReadUInt32();
            for (int k = 0; k < influences; k++) bones[i * 12 + k] = anim < IncreaseBoneIndexLimitPerChunk ? r.ReadByte() : r.ReadUInt16();
            if (wideWeights) for (int k = 0; k < 12; k++) weights[i * 12 + k] = r.ReadUInt16();
            else for (int k = 0; k < influences; k++) { byte w = r.ReadByte(); weights[i * 12 + k] = (ushort)(w << 8 | w); }
        }
        return new Dictionary<string, object> { ["Positions"] = position, ["TangentX"] = tx, ["TangentY"] = ty, ["TangentZ"] = tz,
            ["UVs"] = uv, ["Colors"] = color, ["InfluenceBones"] = bones, ["InfluenceWeights"] = weights };
    }

    /// <summary>SkeletalMesh.cpp operator&lt;&lt;(FArchive&amp;, FMeshToMeshVertData&amp;): position, normal and tangent barycentric
    /// coordinates with distance, four source vertex indices, then the weight and padding (before WeightFMeshToMeshVertData,
    /// two padding words).</summary>
    static object ReadMeshToMeshVertData(AssetBinaryReader r, UAsset asset)
    {
        var bary = new float[12];
        for (int k = 0; k < 12; k++) bary[k] = r.ReadSingle();
        var source = new ushort[4];
        for (int k = 0; k < 4; k++) source[k] = r.ReadUInt16();
        if ((int)asset.GetCustomVersion<FReleaseObjectVersion>() < (int)FReleaseObjectVersion.WeightFMeshToMeshVertData)
            return new object[] { bary, source, r.ReadUInt32(), r.ReadUInt32() };
        return new object[] { bary, source, r.ReadSingle(), r.ReadUInt32() };
    }

    /// <summary>SkeletalMeshLODModel.cpp operator&lt;&lt;(FArchive&amp;, FSkelMeshSourceSectionUserData&amp;).</summary>
    static Dictionary<string, object> ReadSkelMeshSourceSectionUserData(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var u = new Dictionary<string, object>();
        if ((c.StripFlags(u)[0] & 1) != 0) return u;
        u["bRecomputeTangent"] = c.Bool();
        if (asset.GetCustomVersion(RecomputeTangentGuid) >= RecomputeTangentVertexColorMask) u["RecomputeTangentsVertexMaskChannel"] = r.ReadByte();
        u["bCastShadow"] = c.Bool();
        if (asset.GetCustomVersion(UE5MainGuid) >= SkelMeshSectionVisibleInRayTracingFlagAdded) u["bVisibleInRayTracing"] = c.Bool();
        u["bDisabled"] = c.Bool();
        u["GenerateUpToLodIndex"] = r.ReadInt32();
        u["CorrespondClothAssetIndex"] = r.ReadInt16();
        u["ClothingData"] = new object[] { r.ReadGuid(), r.ReadInt32() };
        return u;
    }

    /// <summary>SkinWeightProfile.cpp operator&lt;&lt;(FArchive&amp;, FRawSkinWeight&amp;): bone indices and weights per influence.</summary>
    static object ReadRawSkinWeight(AssetBinaryReader r, UAsset asset)
    {
        var bones = new ushort[12]; var weights = new ushort[12];
        int anim = asset.GetCustomVersion(AnimGuid);
        if (anim < UnlimitedBoneInfluences)
            for (int k = 0; k < 8; k++) { bones[k] = anim < IncreaseBoneIndexLimitPerChunk ? r.ReadByte() : r.ReadUInt16(); byte w = r.ReadByte(); weights[k] = (ushort)(w << 8 | w); }
        else if (asset.GetCustomVersion(UE5MainGuid) < IncreasedSkinWeightPrecision)
            for (int k = 0; k < 12; k++) { bones[k] = r.ReadUInt16(); byte w = r.ReadByte(); weights[k] = (ushort)(w << 8 | w); }
        else
            for (int k = 0; k < 12; k++) { bones[k] = r.ReadUInt16(); weights[k] = r.ReadUInt16(); }
        return new object[] { bones, weights };
    }

    /// <summary>
    /// Chaos Field/FieldSystemNodes.cpp: the node FieldNodeFactory makes for a field type and serialization type, then its
    /// Serialize: the name (FFieldNodeBase), then its own fields; child nodes (SerializeInternal) are their field type,
    /// serialization type and node, enums a byte.
    /// </summary>
    static object FieldNode(Cursor c, AssetBinaryReader r, byte fieldType, byte type, int depth)
    {
        if (type == 0) return null;
        if (depth > 64)
        {
            r.Asset.Coverage.Report("pruned-depth", "Field node tree decoding stopped at depth 65 (limit 64).");
            throw new FormatException("Field node tree nested too deeply");
        }
        object Child() => FieldNode(c, r, r.ReadByte(), r.ReadByte(), depth + 1);
        var n = new List<object> { fieldType, type, c.Name() };
        switch (type)
        {
            case 1: n.Add(r.ReadInt32()); break;                                                                    // FUniformInteger
            case 2: n.Add(r.ReadSingle()); n.Add(c.Vector()); n.Add(r.ReadInt32()); n.Add(r.ReadInt32()); n.Add(r.ReadByte()); break; // FRadialIntMask
            case 3: n.Add(r.ReadSingle()); break;                                                                   // FUniformScalar
            case 4: n.Add(c.Floats(5)); n.Add(c.Vector()); n.Add(r.ReadByte()); break;                              // FRadialFalloff
            case 5: n.Add(c.Floats(5)); n.Add(c.Vector()); n.Add(c.Vector()); n.Add(r.ReadByte()); break;           // FPlaneFalloff
            case 6: n.Add(c.Floats(4)); n.Add(c.Transform()); n.Add(r.ReadByte()); break;                          // FBoxFalloff
            case 7: n.Add(c.Floats(2)); n.Add(c.Transform()); break;                                                // FNoiseField
            case 8: case 9: n.Add(r.ReadSingle()); n.Add(c.Vector()); break;                                        // FUniformVector, FRadialVector
            case 10: n.Add(r.ReadSingle()); break;                                                                  // FRandomVector
            case 11: n.Add(r.ReadSingle()); n.Add(Child()); n.Add(Child()); n.Add(r.ReadByte()); break;            // FSumScalar
            case 12: n.Add(r.ReadSingle()); n.Add(Child()); n.Add(Child()); n.Add(Child()); n.Add(r.ReadByte()); break; // FSumVector
            case 13: n.Add(Child()); break;                                                                         // FConversionField
            case 14: n.Add(Child()); n.Add(Child()); n.Add(r.ReadByte()); break;                                    // FCullingField
            case 15: n.Add(r.ReadSingle()); n.Add(c.Vector()); n.Add(r.ReadSingle()); n.Add(r.ReadSingle()); n.Add(r.ReadByte()); n.Add(r.ReadByte()); break; // FWaveScalar
            case 16: break;                                                                                         // FReturnResultsTerminal
            case 17: n.Add(r.ReadSingle()); n.Add(c.Vector()); n.Add(r.ReadSingle()); n.Add(c.Vector()); break;    // FRotatedRadialVector
            default: throw new FormatException($"Unknown field node serialization type {type}");
        }
        return n;
    }

    const int MantleDbSerialize = 17;
    static readonly Guid MetaHumanCharacterGuid = UAPUtils.GUID(0x0C5DB848, 0x021757CE, 0x0C42843E, 0x0CA16973);
    static readonly Guid EnterpriseGuid = UAPUtils.GUID(0x9DFFBCD6, 0x494F0158, 0xE2211282, 0x3C92A888);

    /// <summary>
    /// SoundWave.cpp USoundWave::Serialize: the packed flags (cooked, owner loading behaviour), a legacy compression name,
    /// then uncooked the raw wave data (FEditorBulkData; before SoundWaveVirtualizationUpdate an FByteBulkData) and the
    /// compressed data GUID. Cue points and compressed formats are only in cooked packages.
    /// </summary>
    static void ReadSoundWave(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, Func<byte[]> rest)
    {
        uint flags = r.ReadUInt32();
        d["Flags"] = flags;
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SOUND_COMPRESSION_TYPE_ADDED && asset.GetCustomVersion(FrameworkGuid) < 12) d["LegacyCompressionName"] = c.Name();
        if ((flags & 1) != 0) { d["CookedData"] = rest(); return; }
        d["RawData"] = asset.GetCustomVersion(UE5MainGuid) < 66 ? c.ByteBulk("sound wave audio") : c.EditorBulk();
        d["CompressedDataGuid"] = r.ReadGuid();
    }

    /// <summary>
    /// CurveTable.cpp UCurveTable::Serialize: the row count, the mode (from ShrinkCurveTableSize; before it rich curves), then
    /// each row's name and the tagged properties of its FSimpleCurve or FRichCurve.
    /// </summary>
    static void ReadCurveTable(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int rows = c.Count(9);
        byte mode = asset.GetCustomVersion(FortniteMainGuid) >= 23 ? r.ReadByte() : (byte)2;
        d["CurveTableMode"] = mode;
        var list = new List<object>(rows);
        for (int i = 0; i < rows; i++) list.Add(new object[] { c.Name(), c.Tagged(mode == 1 ? "SimpleCurve" : "RichCurve") });
        d["Rows"] = list;
    }

    /// <summary>NNEModelData.cpp UNNEModelData::Serialize, by the asset's own version (V0 to V5).</summary>
    static void ReadNNEModelData(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int v = asset.GetCustomVersion(NNEModelDataGuid);
        if (v < 0 || v > 5) throw new FormatException($"Unknown NNE model data version {v}");
        if (v >= 1) d["TargetRuntimes"] = c.Array(c.Str, 4);
        if (v == 1) d["AssetImportData"] = new FPackageIndex(r);
        d["FileType"] = c.Str();
        d["FileData"] = new Payload("NNE model file", v >= 4 ? c.Bytes64() : c.Bytes());
        if (v >= 3) d["AdditionalFileData"] = c.Array(() => new object[] { c.Str(), new Payload("NNE model file", v >= 4 ? c.Bytes64() : c.Bytes()) }, 8);
        if (v >= 5) d["RuntimeSettings"] = c.Array(() => new object[] { c.Str(), new Payload("NNE runtime settings", c.Bytes64()) }, 12);
        d["FileId"] = r.ReadGuid();
        d["ModelData"] = c.Array<object>(() =>
        {
            string name = c.Str();
            if (v < 2) return new object[] { name, c.Bytes() };
            uint alignment = r.ReadUInt32();
            long size = r.ReadInt64();
            if (size < 0 || size > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Invalid model data size {size}");
            return new object[] { name, alignment, r.ReadBytes((int)size) };
        }, 4);
    }

    /// <summary>
    /// Interchange Types/AttributeStorage.h operator&lt;&lt;(FAttributeStorage): the fragmented memory cost, the defragment
    /// ratio, the allocation table (each key's offsets and sizes, a single pair before MultipleAllocationsPerAttributeInStorageFixed,
    /// type, property and hash) and the storage.
    /// </summary>
    /// <summary>
    /// Interchange AttributeStorage.h FAttributeStorage: the fragmented memory cost, defrag ratio, the allocation table (each
    /// key with its allocations' offsets and sizes, one of each before MultipleAllocationsPerAttributeInStorageFixed, its type,
    /// property flags and hash) and the storage buffer. Each value sits in the buffer as the saving editor held it in memory:
    /// an FString as NUL-terminated UTF-16, an FName or soft object path as its string, a string array as one allocation per
    /// element, an FInterchangeLODMeshData as its mesh UID, scene node name, transform and geometric transform, any other type
    /// as its bytes; freed allocations stay in the buffer as fragmented memory.
    /// </summary>
    static object ReadAttributeStorage(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        bool multiple = asset.GetCustomVersion(InterchangeGuid) >= 3;
        var d = new Dictionary<string, object> { ["FragmentedMemoryCost"] = r.ReadUInt64(), ["DefragRatio"] = r.ReadSingle() };
        var table = c.Array(() => (Key: c.Str(),
            Offsets: multiple ? c.Array(() => r.ReadUInt64(), 8) : new List<ulong> { r.ReadUInt64() },
            Sizes: multiple ? c.Array(() => r.ReadUInt64(), 8) : new List<ulong> { r.ReadUInt64() },
            Type: r.ReadInt32(), Property: r.ReadUInt32(), Hash: r.ReadGuid()), 36);
        byte[] storage = c.Bytes64();
        var interpreted = new List<(ulong Start, ulong End)>();
        d["Attributes"] = table.Select(e => new Dictionary<string, object> { ["Key"] = e.Key, ["Type"] = AttributeTypeName(e.Type),
            ["Property"] = e.Property, ["Hash"] = e.Hash, ["Value"] = AttributeValue(e.Type, e.Offsets, e.Sizes, storage, r, interpreted) }).ToList();
        ulong consumed = 0;
        foreach (var range in interpreted.OrderBy(range => range.Start))
        {
            if (range.Start > consumed)
                asset.Coverage.Report("discarded-native-buffer", $"Interchange attribute storage bytes [{consumed}, {range.Start}) are not represented by decoded attributes.");
            consumed = Math.Max(consumed, range.End);
        }
        if (consumed < (ulong)storage.LongLength)
            asset.Coverage.Report("discarded-native-buffer", $"Interchange attribute storage bytes [{consumed}, {storage.LongLength}) are not represented by decoded attributes.");
        d["StorageSize"] = storage.LongLength;
        return d;
    }

    // AttributeStorage.h EAttributeTypes, single then array, with each value's in-memory element layout (b uint8, c int8, h int16,
    // H uint16, i int32, I uint32, q int64, Q uint64, e half, f float, d double, g FGuid) and size including padding
    static readonly Dictionary<int, (string Name, string Layout, int Size)> AttributeTypes = new()
    {
        [1] = ("Bool", "b", 1), [4] = ("Color", "bbbb", 4), [5] = ("DateTime", "q", 8), [6] = ("Double", "d", 8), [8] = ("Float", "f", 4),
        [9] = ("Guid", "g", 16), [10] = ("Int8", "c", 1), [11] = ("Int16", "h", 2), [12] = ("Int32", "i", 4), [13] = ("Int64", "q", 8),
        [14] = ("IntRect", "iiii", 16), [15] = ("LinearColor", "ffff", 16), [17] = ("RandomStream", "ii", 8), [19] = ("Timespan", "q", 8),
        [20] = ("TwoVectors", "dddddd", 48), [21] = ("UInt8", "b", 1), [22] = ("UInt16", "H", 2), [23] = ("UInt32", "I", 4), [24] = ("UInt64", "Q", 8),
        [25] = ("Vector2d", "dd", 16), [26] = ("IntPoint", "ii", 8), [27] = ("IntVector", "iii", 12), [28] = ("Vector2DHalf", "ee", 4),
        [29] = ("Float16", "e", 2), [30] = ("OrientedBox", "ddddddddddddddd", 120), [31] = ("FrameNumber", "i", 4), [32] = ("FrameRate", "ii", 8),
        [33] = ("FrameTime", "if", 8), [35] = ("Matrix44f", "ffffffffffffffff", 64), [36] = ("Matrix44d", "dddddddddddddddd", 128),
        [37] = ("Plane4f", "ffff", 16), [38] = ("Plane4d", "dddd", 32), [39] = ("Quat4f", "ffff", 16), [40] = ("Quat4d", "dddd", 32),
        [41] = ("Rotator3f", "fff", 12), [42] = ("Rotator3d", "ddd", 24), [45] = ("Vector3f", "fff", 12), [46] = ("Vector3d", "ddd", 24),
        [47] = ("Vector2f", "ff", 8), [48] = ("Vector4f", "ffff", 16), [49] = ("Vector4d", "dddd", 32), [50] = ("Box2f", "ffffb", 20),
        [51] = ("Box2D", "ddddb", 40), [52] = ("Box3f", "ffffffb", 28), [53] = ("Box3d", "ddddddb", 56), [54] = ("BoxSphereBounds3f", "fffffff", 28),
        [55] = ("BoxSphereBounds3d", "ddddddd", 56), [56] = ("Sphere3f", "ffff", 16), [57] = ("Sphere3d", "dddd", 32),
    };
    static readonly Dictionary<int, int> AttributeArrayElement = new()
    {
        [58] = 1, [59] = 4, [60] = 5, [61] = 6, [63] = 8, [64] = 9, [65] = 10, [66] = 11, [67] = 12, [68] = 13, [69] = 14, [70] = 15, [72] = 17,
        [74] = 19, [75] = 20, [76] = 22, [77] = 23, [78] = 24, [79] = 25, [80] = 26, [81] = 27, [82] = 28, [83] = 29, [84] = 30, [85] = 31,
        [86] = 32, [87] = 33, [89] = 35, [90] = 36, [91] = 37, [92] = 38, [93] = 39, [94] = 40, [95] = 41, [96] = 42, [99] = 45, [100] = 46,
        [101] = 47, [102] = 48, [103] = 49, [104] = 50, [105] = 51, [106] = 52, [107] = 53, [108] = 54, [109] = 55, [110] = 56, [111] = 57,
    };
    static readonly Dictionary<int, string> AttributeSpecial = new()
    {
        [0] = "None", [2] = "ByteArray", [3] = "ByteArray64", [7] = "Enum", [16] = "Name", [18] = "String", [34] = "SoftObjectPath",
        [43] = "Transform3f", [44] = "Transform3d", [62] = "EnumArray", [71] = "NameArray", [73] = "StringArray", [88] = "SoftObjectPathArray",
        [97] = "Transform3fArray", [98] = "Transform3dArray", [112] = "InterchangeLODMeshData", [113] = "InterchangeLODMeshDataArray",
    };
    static string AttributeTypeName(int type) => AttributeTypes.TryGetValue(type, out var t) ? t.Name
        : AttributeArrayElement.TryGetValue(type, out int e) ? AttributeTypes[e].Name + "Array"
        : AttributeSpecial.TryGetValue(type, out var n) ? n : throw new FormatException($"Invalid attribute type {type}");

    static object AttributeValue(int type, List<ulong> offsets, List<ulong> sizes, byte[] storage, AssetBinaryReader reader, List<(ulong Start, ulong End)> interpreted)
    {
        if (offsets.Count != sizes.Count) throw new FormatException("Attribute offsets and sizes differ in count");
        ReadOnlySpan<byte> At(int i)
        {
            if (offsets[i] > (ulong)storage.LongLength || sizes[i] > (ulong)storage.LongLength - offsets[i]) throw new FormatException("Attribute allocation outside the storage");
            if (sizes[i] > 0) interpreted.Add((offsets[i], offsets[i] + sizes[i]));
            return storage.AsSpan((int)offsets[i], (int)sizes[i]);
        }
        string Text(int i)
        {
            var b = At(i);
            if (b.Length % 2 != 0 || b.Length < 2 || b[^1] != 0 || b[^2] != 0) throw new FormatException("Attribute string is not NUL-terminated UTF-16");
            return reader.DecodeString(System.Text.Encoding.Unicode, b[..^2]);
        }
        object Elements(ReadOnlySpan<byte> b, string layout, int size, bool single)
        {
            if (b.Length % size != 0 || (single && b.Length != size)) throw new FormatException($"Attribute of {b.Length} bytes is not made of {size}-byte values");
            var values = new List<object>(b.Length / size);
            for (int at = 0; at < b.Length; at += size)
            {
                var v = new object[layout.Length];
                int p = at;
                for (int k = 0; k < layout.Length; k++)
                {
                    char ch = layout[k];
                    // members are aligned to their own size, as the compiler lays them out
                    int w = ch switch { 'b' or 'c' => 1, 'h' or 'H' or 'e' => 2, 'i' or 'I' or 'f' => 4, 'g' => 16, _ => 8 };
                    int align = ch == 'g' ? 4 : w;
                    p = at + ((p - at + align - 1) / align) * align;
                    v[k] = ch switch
                    {
                        'b' => b[p], 'c' => (sbyte)b[p], 'h' => BitConverter.ToInt16(b[p..]), 'H' => BitConverter.ToUInt16(b[p..]),
                        'e' => (float)BitConverter.ToHalf(b[p..]), 'i' => BitConverter.ToInt32(b[p..]), 'I' => BitConverter.ToUInt32(b[p..]),
                        'f' => BitConverter.ToSingle(b[p..]), 'g' => new Guid(b.Slice(p, 16)), 'q' => BitConverter.ToInt64(b[p..]),
                        'Q' => BitConverter.ToUInt64(b[p..]), _ => BitConverter.ToDouble(b[p..]),
                    };
                    p += w;
                }
                if (p - at > size) throw new FormatException($"Attribute layout {layout} exceeds {size} bytes");
                values.Add(layout.Length == 1 ? v[0] : v);
            }
            return single ? values[0] : values;
        }
        // FTransform: rotation, translation and scale, each a four-lane vector register when vectorized, otherwise three lanes
        object Transforms(ReadOnlySpan<byte> b, bool wide, bool single)
        {
            int lane = wide ? 8 : 4;
            string layout = new string(wide ? 'd' : 'f', b.Length % (12 * lane) == 0 ? 12 : 10);
            return Elements(b, layout, layout.Length * lane, single);
        }
        if (AttributeTypes.TryGetValue(type, out var t)) return Elements(At(0), t.Layout, t.Size, true);
        if (AttributeArrayElement.TryGetValue(type, out int e)) { var et = AttributeTypes[e]; return Elements(At(0), et.Layout, et.Size, false); }
        switch (type)
        {
            case 0: return null;
            case 2: case 3: return new Payload("byte array attribute", At(0).ToArray());
            case 7:
            {
                // an enum is stored as its underlying integer
                var b = At(0);
                return b.Length switch { 1 => b[0], 2 => BitConverter.ToUInt16(b), 4 => BitConverter.ToUInt32(b), 8 => (object)BitConverter.ToUInt64(b),
                    _ => throw new FormatException($"Enum attribute of {b.Length} bytes") };
            }
            case 62: return new Payload("enum array attribute (element width set by its enum type)", At(0).ToArray());
            case 16: case 18: case 34: return Text(0);
            case 71: case 73: case 88: return Enumerable.Range(0, offsets.Count).Select(Text).ToList();
            case 43: case 97: return Transforms(At(0), false, type == 43);
            case 44: case 98: return Transforms(At(0), true, type == 44);
            case 112: case 113:
            {
                if (offsets.Count % 4 != 0) throw new FormatException("LOD mesh data without four allocations per entry");
                var list = new List<object>();
                for (int i = 0; i < offsets.Count; i += 4)
                    list.Add(new Dictionary<string, object> { ["MeshUid"] = Text(i), ["SceneNodeName"] = Text(i + 1),
                        ["Transform"] = Transforms(At(i + 2), true, true), ["GeometricTransform"] = Transforms(At(i + 3), true, true) });
                return type == 112 ? list.Single() : list;
            }
            default: throw new FormatException($"Invalid attribute type {type}");
        }
    }

    /// <summary>
    /// MediaAssets BaseMediaSource.cpp UBaseMediaSource::Serialize: before RenameMediaSourcePlatformPlayers a dummy platform
    /// player map (editor data) and default player; then without editor data the player name, with it the platform players
    /// by name, from SerializeGUIDsInMediaSourceInsteadOfPlainNames by GUID.
    /// </summary>
    static void ReadBaseMediaSource(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        if (asset.GetCustomVersion(SequencerGuid) < 1)
        {
            if (!asset.IsFilterEditorOnly) d["DummyPlatformPlayers"] = c.Array(() => new object[] { r.ReadGuid(), r.ReadGuid() }, 32);
            d["DummyDefaultPlayer"] = c.Str();
        }
        else if (asset.IsFilterEditorOnly) d["PlayerName"] = c.Name();
        else if (asset.GetCustomVersion(MediaGuid) < 1) d["PlatformPlayerNames"] = c.Array(() => new object[] { c.Str(), c.Name() }, 12);
        else d["PlatformGuidPlayers"] = c.Array(() => new object[] { r.ReadGuid(), r.ReadGuid() }, 32);
    }

    /// <summary>
    /// MeshDescription.cpp FMeshDescriptionBulkData::Serialize: the bulk data (an FByteBulkData before MeshDescriptionVirtualization),
    /// the GUID from MeshDescriptionBulkDataGuid and whether it is a hash from MeshDescriptionBulkDataGuidIsHash.
    /// </summary>
    internal static object ReadMeshDescriptionBulkData(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var e = new List<object> { asset.GetCustomVersion(UE5MainGuid) < 41 ? c.ByteBulk() : c.EditorBulk() };
        if (asset.GetCustomVersion(EditorGuid) >= 29) e.Add(r.ReadGuid());
        if (asset.GetCustomVersion(EnterpriseGuid) >= 8) e.Add(c.Bool());
        return e;
    }

    /// <summary>
    /// Whether compressed morph buffers follow, then MorphTargetVertexInfoBuffers.cpp FMorphTargetVertexInfoBuffers::Serialize:
    /// the packed morph data, per-morph minimum and maximum values, batch offsets and counts, the batch total and the
    /// position and tangent precision.
    /// </summary>
    static object MorphBuffers(Cursor c, AssetBinaryReader r)
    {
        if (!c.Bool()) return null;
        return new object[] { c.Array(() => r.ReadUInt32(), 4), c.Array(() => c.Floats(4), 16), c.Array(() => c.Floats(4), 16),
            c.Array(() => r.ReadUInt32(), 4), c.Array(() => r.ReadUInt32(), 4), r.ReadUInt32(), r.ReadSingle(), r.ReadSingle() };
    }

    /// <summary>
    /// HairStrandsCore GroomAsset.cpp UGroomAsset::Serialize, from GroomWithDescription: strip flags, the class strip flags
    /// (from GroomLODStripping), then without editor data the platform data, for a legacy groom without imported strands its
    /// legacy data, otherwise the source hair description bulk data and, when HairDescriptionType is Edit, the edited one.
    /// </summary>
    static void ReadGroomAsset(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, NormalExport export, Func<byte[]> rest)
    {
        if (asset.GetCustomVersion(PhysicsGuid) < 3) throw new FormatException("Groom asset saved before GroomWithDescription");
        var strip = c.StripFlags(d);
        byte classFlags = 1;
        if (asset.GetCustomVersion(AnimGuid) >= 14) { classFlags = r.ReadByte(); d["ClassDataStripFlags"] = classFlags; }
        if ((strip[0] & 1) != 0) { d["PlatformData"] = rest(); return; }
        if (asset.GetCustomVersion(UE5MainGuid) < 110 && (classFlags & 1) == 0) { d["LegacyData"] = rest(); return; }
        object HairDescription() => asset.GetCustomVersion(UE5MainGuid) < 92 ? new object[] { c.ByteBulk("groom hair description"), r.ReadGuid() } : c.EditorBulk();
        d["SourceHairDescription"] = HairDescription();
        string type = (export.Data?.Find(p => p.Name?.ToString() == "HairDescriptionType") as EnumPropertyData)?.Value?.ToString();
        if (type != null && type.EndsWith("Edit")) d["EditHairDescription"] = HairDescription();
    }

    /// <summary>
    /// Dataflow DataflowGraph.cpp FGraph::Serialize and SerializeForLoading: the graph GUID, the topology GUID (from
    /// DataflowAddGraphTopologyGuid), the nodes (GUID, type, name and a sized block) and the connections (input node, output
    /// node, input, output). From DataflowSeparateInputOutputSerialization a node's block is SerializeInternal (the node
    /// struct's tagged properties and its own Serialize) followed by its outputs and inputs (GUID, type, name, any-type flag
    /// from DataflowAnyTypeSupport, hidden flag from DataflowHideablePins).
    /// </summary>
    static object ReadDataflowGraph(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var g = new Dictionary<string, object> { ["Guid"] = r.ReadGuid() };
        int fn = asset.GetCustomVersion(FortniteMainGuid);
        if (fn >= 236) g["GraphTopologyGuid"] = r.ReadGuid();
        g["Nodes"] = c.Array<object>(() =>
        {
            var guid = r.ReadGuid(); var type = c.Name(); var name = c.Name();
            long size = r.ReadInt64();
            long end = r.BaseStream.Position + size;
            if (size < 0 || end > r.BaseStream.Length) throw new FormatException($"Invalid dataflow node size {size}");
            object body;
            if (asset.GetCustomVersion(UE5MainGuid) < 115) body = r.ReadBytes((int)size);
            else
            {
                string typeName = type.ToString();
                string structName = typeName.Length > 1 && typeName[0] == 'F' && char.IsUpper(typeName[1]) ? typeName.Substring(1) : typeName;
                var props = c.Tagged(structName);
                object Pins() => c.Array(() => new object[] { r.ReadGuid(), c.Name(), c.Name(), fn >= 142 ? c.Bool() : null, fn >= 154 ? c.Bool() : null }, 36);
                var outputs = Pins(); var inputs = Pins();
                if (r.BaseStream.Position != end) throw new FormatException($"Dataflow node {typeName} has {end - r.BaseStream.Position} bytes its pins don't account for");
                body = new object[] { props, outputs, inputs };
            }
            return new object[] { guid, type, name, body };
        }, 36);
        g["Connections"] = c.Array(() => new object[] { r.ReadGuid(), r.ReadGuid(), r.ReadGuid(), r.ReadGuid() }, 64);
        return g;
    }

    /// <summary>
    /// GeometryCollectionObject.cpp UGeometryCollection::Serialize after the tagged properties: the cooked flag (from
    /// GeometryCollectionInDDC), the collection (from GeometryCollectionInDDCAndAsset, geometry through a Chaos archive) and,
    /// from GeometryCollectionNaniteTransient, the render data's cooked flag, which an editor save writes false.
    /// </summary>
    /// <summary>
    /// MapBuildData.cpp UMapBuildDataRegistry::Serialize, after its strip flags unless audio-visual data is stripped: the mesh
    /// build data, precomputed light volumes, volumetric lightmaps (from VolumetricLightmaps), light build data, reflection
    /// captures (from MoveReflectionCaptureDataToMapBuildData), sky atmosphere entries (from SkyAtmosphereStaticLightingVersioning,
    /// which store nothing) and the volumetric lightmap grid (from VolumetricLightMapGridDescSupport), each map keyed by GUID.
    /// </summary>
    static void ReadMapBuildData(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int rendering = asset.GetCustomVersion(RenderingGuid), mobile = asset.GetCustomVersion(MobileGuid), release = asset.GetCustomVersion(UE5ReleaseGuid);
        int main = asset.GetCustomVersion(UE5MainGuid);
        var ue = asset.ObjectVersion;
        object Object() => new FPackageIndex(r);
        List<Guid> Guids() => c.Array(r.ReadGuid, 16);
        List<object[]> Map(Func<object> value, int minBytes) => c.Array(() => new object[] { r.ReadGuid(), value() }, 16 + minBytes);
        // LightMap.cpp FLightMap2D::Serialize, and the legacy 1D light map read only to be discarded
        object LightMap()
        {
            uint type = r.ReadUInt32();
            if (type == 0) return null;
            var m = new Dictionary<string, object> { ["Type"] = type, ["LightGuids"] = Guids() };
            if (type == 1)
            {
                m["Owner"] = Object();
                m["DirectionalSamples"] = c.ByteBulk("quantized directional light samples");
                m["Dummy"] = c.Fixed(5, c.Vector);
                m["SimpleSamples"] = c.ByteBulk("quantized simple light samples");
                return m;
            }
            if (type != 2) throw new FormatException($"Invalid light map type {type}");
            if (ue < ObjectVersion.VER_UE4_COMBINED_LIGHTMAP_TEXTURES)
                m["LegacyCoefficients"] = c.Fixed(ue < ObjectVersion.VER_UE4_LOW_QUALITY_DIRECTIONAL_LIGHTMAPS ? 3 : 4, () => new object[] { Object(), c.Vector4(), c.Vector4() });
            else
            {
                m["Textures"] = new[] { Object(), Object() };
                if (ue >= ObjectVersion.VER_UE4_SKY_LIGHT_COMPONENT)
                {
                    m["SkyOcclusionTexture"] = Object();
                    if (ue >= ObjectVersion.VER_UE4_AO_MATERIAL_MASK) m["AOMaterialMaskTexture"] = Object();
                }
                m["ScaleAddVectors"] = c.Fixed(4, () => new[] { c.Floats(4), c.Floats(4) });
            }
            m["CoordinateScale"] = c.Doubles(2, c.Lwc); m["CoordinateBias"] = c.Doubles(2, c.Lwc);
            if (rendering >= 29) { m["bShadowChannelValid"] = c.Fixed(4, c.Bool); m["InvUniformPenumbraSize"] = c.Floats(4); }
            if (rendering >= 27) m["VirtualTextures"] = rendering >= 45 ? new[] { Object(), Object() } : new[] { Object() };
            return m;
        }
        // ShadowMap.cpp FShadowMap2D::Serialize
        object ShadowMap()
        {
            uint type = r.ReadUInt32();
            if (type == 0) return null;
            if (type != 2) throw new FormatException($"Invalid shadow map type {type}");
            var m = new Dictionary<string, object> { ["LightGuids"] = Guids(), ["Texture"] = Object(),
                ["CoordinateScale"] = c.Doubles(2, c.Lwc), ["CoordinateBias"] = c.Doubles(2, c.Lwc), ["bChannelValid"] = c.Fixed(4, c.Bool) };
            if (ue >= ObjectVersion.VER_UE4_STATIC_SHADOWMAP_PENUMBRA_SIZE) m["InvUniformPenumbraSize"] = c.Floats(4);
            return m;
        }
        d["MeshBuildData"] = Map(() =>
        {
            var m = new Dictionary<string, object> { ["LightMap"] = LightMap(), ["ShadowMap"] = ShadowMap(), ["IrrelevantLights"] = Guids() };
            // TArray::BulkSerialize: the element size, then the FPerInstanceLightmapData (light map and shadow map UV biases)
            int size = r.ReadInt32();
            if (size != 16) throw new FormatException($"Per-instance lightmap data element of {size} bytes");
            int n = c.Count(16);
            m["PerInstanceLightmapData"] = c.Floats(n * 4);
            return m;
        }, 12);
        // PrecomputedLightVolume.cpp: whether valid and initialized, the bounds, sample spacing, SH sample count (from
        // IndirectLightingCache3BandSupport) and the high and low quality samples (low from VOLUME_SAMPLE_LOW_QUALITY_SUPPORT); a
        // sample is its position, radius, RGB SH lighting, packed sky bent normal and directional light shadowing
        d["LevelPrecomputedLightVolumeBuildData"] = Map(() =>
        {
            if (!c.Bool() || !c.Bool()) return null;
            var v = new Dictionary<string, object> { ["Bounds"] = c.Box(), ["SampleSpacing"] = r.ReadSingle() };
            int sh = rendering >= 1 ? r.ReadInt32() : 4;
            if (sh != 9 && sh != 4) throw new FormatException($"Invalid SH sample count {sh}");
            v["NumSHSamples"] = sh;
            object Sample() => new object[] { c.Floats(3), r.ReadSingle(), c.Floats(sh * 3),
                sh == 9 || ue >= ObjectVersion.VER_UE4_SKY_BENT_NORMAL ? r.ReadUInt32() : null,
                sh == 9 || ue >= ObjectVersion.VER_UE4_VOLUME_SAMPLE_LOW_QUALITY_SUPPORT ? r.ReadSingle() : null };
            v["HighQualitySamples"] = c.Array(Sample, 16 + sh * 12);
            if (ue >= ObjectVersion.VER_UE4_VOLUME_SAMPLE_LOW_QUALITY_SUPPORT) v["LowQualitySamples"] = c.Array(Sample, 16 + sh * 12);
            return v;
        }, 4);
        // PrecomputedVolumetricLightmap.cpp: a layer is its texels and pixel format name
        object Layer() { var bytes = c.Bytes(); string format = c.Str(); return new Payload($"volumetric lightmap texels ({format})", bytes); }
        object IntVector() => new[] { r.ReadInt32(), r.ReadInt32(), r.ReadInt32() };
        object VolumetricLightmap()
        {
            if (!c.Bool()) return null;
            var v = new Dictionary<string, object> { ["Bounds"] = c.Box(), ["IndirectionTextureDimensions"] = IntVector(), ["IndirectionTexture"] = Layer(),
                ["BrickSize"] = r.ReadInt32(), ["BrickDataDimensions"] = IntVector(), ["AmbientVector"] = Layer(), ["SHCoefficients"] = c.Fixed(6, Layer),
                ["SkyBentNormal"] = Layer(), ["DirectionalLightShadowing"] = Layer() };
            if (mobile >= 2 && main < 79) v["LQLayers"] = new[] { Layer(), Layer() };
            if (rendering >= 34) { v["SubLevelBrickPositions"] = c.Array(IntVector, 12); v["IndirectionTextureOriginalValues"] = c.Array(() => r.ReadUInt32(), 4); }
            return v;
        }
        if (rendering >= 20) d["LevelPrecomputedVolumetricLightmapBuildData"] = Map(VolumetricLightmap, 4);
        // FStaticShadowDepthMapData: world to light, size and half-float depth samples
        d["LightBuildData"] = Map(() => new Dictionary<string, object> { ["ShadowMapChannel"] = r.ReadInt32(), ["WorldToLight"] = c.Doubles(16, c.Lwc),
            ["ShadowMapSizeX"] = r.ReadInt32(), ["ShadowMapSizeY"] = r.ReadInt32(),
            ["DepthSamples"] = Enumerable.Range(0, c.Count(2)).Select(_ => (float)r.ReadHalf()).ToArray() }, 4 + 16 * 4 + 12);
        // FReflectionCaptureMapBuildData: cubemap size, average brightness, brightness (StoreReflectionCaptureBrightnessForCooking
        // to ExcludeBrightnessFromEncodedHDRCubemap), the full HDR capture, then the encoded capture or, between
        // StoreReflectionCaptureCompressedMobile and StoreReflectionCaptureEncodedHDRDataInRG11B10Format, a texture reference
        if (asset.GetCustomVersion(ReflectionCaptureGuid) >= 1)
            d["ReflectionCaptureBuildData"] = Map(() =>
            {
                var m = new Dictionary<string, object> { ["CubemapSize"] = r.ReadInt32(), ["AverageBrightness"] = r.ReadSingle() };
                if (rendering >= 22 && release < 23) m["Brightness"] = r.ReadSingle();
                m["FullHDRCapturedData"] = new Payload("HDR cubemap texels (FFloat16Color, each mip's six faces)", c.Bytes());
                if (mobile >= 3 && release < 38) m["EncodedCaptureData"] = Object();
                else m["EncodedHDRCapturedData"] = new Payload(release >= 38 ? "HDR cubemap texels (FFloat3Packed RG11B10, each mip's six faces)" : "stripped cubemap texels", c.Bytes());
                return m;
            }, 12);
        if (rendering >= 32) d["SkyAtmosphereBuildData"] = Guids();
        if (asset.GetCustomVersion(FortniteMainGuid) >= 139 && c.Bool())
        {
            // VolumetricLightmapGrid.cpp: the tagged FVolumetricLightMapGridDesc, then each cell's bulk data
            var grid = c.Tagged("VolumetricLightMapGridDesc");
            d["VolumetricLightMapGridDesc"] = grid;
            int cells = grid.Value.FirstOrDefault(p => p.Name.ToString() == "Cells") is ArrayPropertyData a ? a.Value.Length : 0;
            d["VolumetricLightMapGridCellBulkData"] = c.Fixed(cells, () => c.ByteBulk("volumetric lightmap cell (FPrecomputedVolumetricLightmapData)"));
        }
    }

    static void ReadGeometryCollection(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, Func<byte[]> rest)
    {
        int destruction = asset.GetCustomVersion(DestructionGuid), main = asset.GetCustomVersion(UE5MainGuid);
        if (destruction < 4) throw new FormatException("Geometry collection saved before GeometryCollectionInDDCAndAsset");
        if (main == 1 || (main >= 11 && main < 14)) throw new FormatException("Geometry collection with legacy Nanite data");
        d["bIsCookedOrCooking"] = c.Bool();
        // GeometryCollection.cpp FGeometryCollection::Serialize through FTransformCollection: the managed array collection
        d["GeometryCollection"] = ManagedArrayCollectionSerializer.Read(c, r, asset, new ChaosImplicitSerializer(c, r, asset));
        if (main < 14) return;
        // then whether cooked render data follows
        if (c.Bool()) throw new FormatException("Geometry collection with cooked render data");
        d["bCooked"] = false;
    }

    /// <summary>
    /// ReflectionCaptureComponent.cpp SerializeLegacyData, before MoveReflectionCaptureDataToMapBuildData: a legacy flag, the
    /// data's version GUID, the average brightness and the absolute offset of its end; the current version's data is a
    /// validity flag, the cubemap size and the compressed capture.
    /// </summary>
    static void ReadReflectionCaptureLegacy(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, long fileOffset)
    {
        if (asset.GetCustomVersion(ReflectionCaptureGuid) >= 1) return;
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_REFLECTION_CAPTURE_COOKING) d["bLegacy"] = c.Bool();
        if (asset.ObjectVersion < ObjectVersion.VER_UE4_REFLECTION_DATA_IN_PACKAGES) return;
        var version = r.ReadGuid();
        d["SavedVersion"] = version;
        if (asset.GetCustomVersion(RenderingGuid) >= 5) d["AverageBrightness"] = r.ReadSingle();
        long end = r.ReadInt32() - fileOffset;
        if (version != LegacyReflectionCaptureVer)
        {
            if (end < r.BaseStream.Position || end > r.BaseStream.Length) throw new FormatException($"Invalid reflection capture end {end}");
            d["SkippedData"] = new Payload("reflection capture data of another version, which the engine skips", r.ReadBytes((int)(end - r.BaseStream.Position)));
            return;
        }
        bool valid = c.Bool();
        d["bValid"] = valid;
        if (!valid) return;
        if (asset.GetCustomVersion(RenderingGuid) >= 2) d["CubemapSize"] = r.ReadInt32();
        d["CompressedCapturedData"] = new Payload("compressed reflection cubemap", c.Bytes());
    }

    /// <summary>
    /// InstancedStaticMesh.cpp UInstancedStaticMeshComponent::Serialize: the cooked flag (from SerializeInstancedStaticMeshRenderData),
    /// whether per-instance data follows (from ISMComponentEditableWhenInheritedSkipSerialization), the instances (deprecated
    /// layout before InstancedStaticMeshLightmapSerialization; per element before large world coordinates) and their custom data
    /// (from PerInstanceCustomData), and cooked render data.
    /// </summary>
    static void ReadInstancedStaticMesh(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, Func<byte[]> rest)
    {
        bool hasCooked = asset.GetCustomVersion(FortniteMainGuid) >= 11 || asset.GetCustomVersion(EditorGuid) >= 24, cooked = false;
        if (hasCooked) { cooked = c.Bool(); d["bCooked"] = cooked; }
        bool hasData = true;
        if (asset.GetCustomVersion(FortniteMainGuid) >= 115) { hasData = c.Bool(); d["bHasSkipSerializationPropertiesData"] = hasData; }
        if (asset.GetCustomVersion(MobileGuid) < 1) d["PerInstanceSMData_DEPRECATED"] = c.Bulk();
        else if (hasData)
        {
            // FInstancedStaticMeshInstanceData: the instance's FMatrix, single precision before large world coordinates
            bool lwc = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;
            d["PerInstanceSMData"] = c.Bulk(new string(lwc ? 'd' : 'f', 16), lwc ? null : 64);
            if (asset.GetCustomVersion(RenderingGuid) >= 40) d["PerInstanceSMCustomData"] = c.Bulk("f");
        }
        if (cooked) d["RenderData"] = rest();
    }

    /// <summary>
    /// InstancedSkinnedMeshComponent.cpp UInstancedSkinnedMeshComponent::Serialize: the cooked flag, whether instance data
    /// follows, the instances (transform and animation index; bulk serialized in the deprecated layout before
    /// SkinnedMeshInstanceDataSerializationV2) and their custom data, the instance ID map (from that version), the bone
    /// attachment sockets and bindings (from InstancedSkinnedMeshBoneAttachments), and cooked render data.
    /// </summary>
    static void ReadInstancedSkinnedMesh(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, Func<byte[]> rest)
    {
        bool cooked = c.Bool(), hasData = c.Bool();
        bool v2 = asset.GetCustomVersion(UE5MainGuid) >= 120;
        d["bCooked"] = cooked;
        d["bHasSkipSerializationPropertiesData"] = hasData;
        if (hasData)
        {
            d["InstanceData"] = v2 ? c.Array(() => new object[] { c.Floats(10), r.ReadUInt32() }, 44) : c.Bulk();
            d["InstanceCustomData"] = c.Bulk();
        }
        if (v2) d["InstanceIdIndexMap"] = new object[] { c.Array(() => r.ReadInt32(), 4), r.ReadInt32(), r.ReadInt32() };
        if (asset.GetCustomVersion(FortniteMainGuid) >= 264)
        {
            d["BoneAttachmentSockets"] = c.Array(() => new object[] { new FPackageIndex(r), c.Name(), r.ReadInt32() }, 16);
            d["BoneAttachmentBindings"] = c.Array(() => r.ReadUInt32(), 4);
        }
        if (cooked) d["RenderData"] = rest();
    }

    /// <summary>
    /// Foliage InstancedFoliage.cpp AInstancedFoliageActor::Serialize: with editor data the instance base cache (from
    /// CrossLevelBase), then FoliageInfos by foliage type. Each FFoliageInfo: its implementation type and data (a static
    /// mesh's component; an actor implementation's actors and class; an ISM actor's GUID, client handle, definition and
    /// class), then with editor data its instances and FoliageTypeUpdateGuid.
    /// </summary>
    static void ReadInstancedFoliageActor(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int foliage = asset.GetCustomVersion(FoliageGuid);
        bool soft = asset.GetCustomVersion(FortniteMainGuid) >= 17, editor = !asset.IsFilterEditorOnly;
        if (editor && foliage >= 5)
            d["InstanceBaseCache"] = new object[] { r.ReadInt32(),
                c.Array(() => new object[] { r.ReadInt32(), soft ? new FSoftObjectPath(r) : r.ReadGuid(), c.Vector(), c.Vector(), c.Vector() }, 60),
                c.Array(() => new object[] { soft ? new FSoftObjectPath(r) : r.ReadGuid(), c.Array<object>(() => soft ? new FSoftObjectPath(r) : r.ReadGuid(), 4) }, 8) };
        if (asset.ObjectVersion < ObjectVersion.VER_UE4_FOLIAGE_SETTINGS_TYPE || foliage < 12) throw new FormatException("Foliage saved before FoliageActorSupport");
        d["FoliageInfos"] = c.Array<object>(() =>
        {
            var info = new List<object> { new FPackageIndex(r) };
            byte type = r.ReadByte();
            info.Add(type);
            switch (type)
            {
                case 0: break;
                case 1: info.Add(new FPackageIndex(r)); break;
                case 2: info.Add(c.Array(() => new FPackageIndex(r), 4)); info.Add(new FPackageIndex(r)); break;
                case 3:
                    info.Add(r.ReadGuid()); info.Add(new object[] { r.ReadInt32(), r.ReadGuid() });
                    info.Add(c.Array(() => new object[] { r.ReadInt32(), c.Array(c.Transform, 40) }, 8));
                    info.Add(new FPackageIndex(r));
                    break;
                default: throw new FormatException($"Invalid foliage implementation type {type}");
            }
            if (editor)
            {
                info.Add(c.Array(() => new object[] { c.Vector(), c.Vector(), c.Floats(3), c.Vector(), r.ReadGuid(), r.ReadUInt32(), r.ReadSingle(), r.ReadInt32() }, 64));
                info.Add(r.ReadGuid());
            }
            return info;
        }, 5);
    }

    /// <summary>
    /// NavigationSystem NavCollision.cpp UNavCollision::Serialize: a magic number and version (absent in the initial version),
    /// a legacy GUID, the cooked flag, cooked collision formats when cooked and convex geometry applies, and the area class
    /// (from VerAreaClass).
    /// </summary>
    static void ReadNavCollision(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, NormalExport export)
    {
        long start = r.BaseStream.Position;
        int version = 1;
        if (r.ReadUInt32() == 0xA237F237) version = r.ReadInt32();
        else r.BaseStream.Position = start;
        d["Version"] = version;
        d["Guid"] = r.ReadGuid();
        bool cooked = c.Bool();
        d["bCooked"] = cooked;
        if (cooked)
        {
            bool gather = export.Data?.Find(p => p.Name?.ToString() == "bGatherConvexGeometry") is not BoolPropertyData b || b.Value;
            int Count(string name) => export.Data?.Find(p => p.Name?.ToString() == name) is ArrayPropertyData a ? a.Value.Length : 0;
            int cylinders = Count("CylinderCollision"), boxes = Count("BoxCollision");
            bool process = version >= 4 ? gather || boxes > 0 || cylinders > 0 : gather || (cylinders == 0 && boxes == 0);
            if (process) d["CookedFormatData"] = c.Array(() => new object[] { c.Name(), c.ByteBulk("cooked navigation collision") }, 24);
        }
        if (version >= 2) d["AreaClass"] = new FPackageIndex(r);
    }

    /// <summary>
    /// Model.cpp UModel::Serialize: strip flags, bounds, the BSP vectors, points and nodes, surfaces and vertices, the shared
    /// side count, with editor data the polys, leaf hulls and leaves, the root-outside and linked flags, the unique vertex
    /// count, the vertex buffer unless stripped, the lighting GUID and the lightmass settings.
    /// </summary>
    static void ReadModel(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var strip = c.StripFlags(d);
        bool lwc = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;
        d["Bounds"] = new object[] { c.Vector(), c.Vector(), lwc ? r.ReadDouble() : r.ReadSingle() };
        if (asset.ObjectVersion < ObjectVersion.VER_UE4_BSP_UNDO_FIX || asset.ObjectVersion < ObjectVersion.VER_UE4_REMOVE_ZONES_FROM_MODEL)
            throw new FormatException("Model saved before REMOVE_ZONES_FROM_MODEL");
        // Model.h: FVector3f vectors and points; FBspNode: plane, vertex pool, surface, vertex index, component index and node
        // index (uint16), component element, back, front, plane, collision bound, two zones, vertex count, flags, two leaves
        d["Vectors"] = c.Bulk("fff"); d["Points"] = c.Bulk("fff");
        d["Nodes"] = c.Bulk("ffffiiihhiiiiibbbbii");
        d["Surfs"] = c.Array(() => new object[] { new FPackageIndex(r), r.ReadUInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), new FPackageIndex(r), c.Floats(4), r.ReadSingle(), r.ReadInt32() }, 56);
        // FVert: point index, side, shadow and back-face shadow texture coordinates
        d["Verts"] = c.Bulk("iiffff");
        d["NumSharedSides"] = r.ReadInt32();
        if (!asset.IsFilterEditorOnly || asset.ObjectVersion < ObjectVersion.VER_UE4_REMOVE_UNUSED_UPOLYS_FROM_UMODEL)
        {
            d["Polys"] = new FPackageIndex(r); d["LeafHulls"] = c.Bulk("i"); d["Leaves"] = c.Bulk("i");
        }
        d["RootOutside"] = c.Bool(); d["Linked"] = c.Bool();
        d["NumUniqueVertices"] = r.ReadUInt32();
        if ((strip[0] & 1) == 0 || (strip[1] & 1) == 0)
            d["VertexBuffer"] = asset.GetCustomVersion(RenderingGuid) < 26 ? c.Bulk() : c.Array(() => c.Floats(14), 56);
        d["LightingGuid"] = r.ReadGuid();
        d["LightmassSettings"] = c.Array(() => Lightmass(c, r, asset), 32);
    }

    /// <summary>Polygon.cpp operator&lt;&lt;(FPoly).</summary>
    static object Poly(Cursor c, AssetBinaryReader r, UAsset asset) => new object[] { c.Floats(12), c.Array(() => c.Floats(3), 12),
        r.ReadUInt32(), new FPackageIndex(r), c.Name(), new FPackageIndex(r), r.ReadInt32(), r.ReadInt32(), r.ReadSingle(), Lightmass(c, r, asset), c.Name() };

    /// <summary>Polygon.cpp operator&lt;&lt;(FLightmassPrimitiveSettings).</summary>
    static object Lightmass(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var l = new List<object> { c.Bool(), c.Bool(), r.ReadSingle(), c.Bool() };
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_NEW_LIGHTMASS_PRIMITIVE_SETTING) l.Add(c.Bool());
        l.Add(r.ReadSingle()); l.Add(r.ReadSingle()); l.Add(r.ReadSingle()); l.Add(r.ReadSingle());
        return l;
    }

    /// <summary>
    /// Landscape.cpp ULandscapeComponent::Serialize after the tagged properties: the grass data (from SERIALIZE_LANDSCAPE_GRASS_DATA)
    /// and the cooked flag (from LANDSCAPE_PLATFORMDATA_COOKING). LandscapeGrass.cpp operator&lt;&lt;(FLandscapeComponentGrassData):
    /// with editor data the material state IDs and WPO rotation, from LandscapeSupportPerComponentGrassTypes a generation hash;
    /// legacy height data before LandscapeGrassSingleArray; with editor data the height mips; then the element count, the
    /// weight offsets (by grass type before OverridableLandscapeGrassTypes, by name from it) and the height and weight data.
    /// </summary>
    static void ReadLandscapeComponent(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, NormalExport export)
    {
        if (asset.GetCustomVersion(RenderingGuid) < 9) throw new FormatException("Landscape component saved before MapBuildDataSeparatePackage");
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_LANDSCAPE_GRASS_DATA)
        {
            int fn = asset.GetCustomVersion(FortniteMainGuid), land = asset.GetCustomVersion(LandscapeGuid);
            bool editor = !asset.IsFilterEditorOnly;
            var g = new Dictionary<string, object>();
            if (editor)
            {
                if (fn < 116)
                {
                    if (land >= 5) g["MaterialStateIds"] = c.Array(() => r.ReadGuid(), 16);
                    else if (asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_LANDSCAPE_GRASS_DATA_MATERIAL_GUID) g["MaterialStateId"] = r.ReadGuid();
                    if (land >= 2) g["RotationForWPO"] = c.Doubles(4, asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES);
                }
                else g["GenerationHash"] = r.ReadUInt32();
            }
            if (fn < 54) g["DeprecatedHeightData"] = c.Bulk();
            if (editor && land >= 3)
            {
                if (land >= 4) g["HeightMipData"] = c.Array(() => new object[] { r.ReadInt32(), c.Array(() => r.ReadUInt16(), 2) }, 8);
                else { g["CollisionHeightData"] = c.Bulk(); g["SimpleCollisionHeightData"] = c.Bulk(); }
            }
            if (fn < 54) g["DeprecatedWeightData"] = c.Array(() => new object[] { new FPackageIndex(r), new Payload("landscape weightmap", c.Bytes()) }, 8);
            else
            {
                g["NumElements"] = r.ReadInt32();
                g["WeightOffsets"] = c.Array(() => new object[] { fn < 253 ? new FPackageIndex(r) : c.Name(), r.ReadInt32() }, 8);
                g["HeightWeightData"] = new Payload("landscape height and weight data", c.Bytes());
            }
            d["GrassData"] = g;
        }
        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_LANDSCAPE_PLATFORMDATA_COOKING) d["bCooked"] = c.Bool();
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
        public bool Lwc => r.Asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES;
        /// <summary>An FVector or FRotator: doubles with large world coordinates, floats before.</summary>
        public double[] Vector() => Doubles(3, Lwc);
        public double[] Vector4() => Doubles(4, Lwc);
        public double[] Doubles(int n, bool wide) { var v = new double[n]; for (int i = 0; i < n; i++) v[i] = wide ? r.ReadDouble() : r.ReadSingle(); return v; }
        public float[] Floats(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = r.ReadSingle(); return v; }
        public List<T> Fixed<T>(int n, Func<T> read) { var l = new List<T>(n); for (int i = 0; i < n; i++) l.Add(read()); return l; }
        /// <summary>An FBox: minimum, maximum and the validity byte.</summary>
        public object Box() => new object[] { Vector(), Vector(), r.ReadByte() };
        /// <summary>A TOptional: whether it is set, then the value.</summary>
        public object Optional(Func<object> read) => Bool() ? read() : null;
        public object Optional(Func<string> read) => Bool() ? read() : null;
        public byte[] Bytes() { int n = Count(1); return r.ReadBytes(n); }
        /// <summary>
        /// Archive.cpp FArchive::SerializeCompressedNew: the package file tag (with the chunk size; a v2 tag adds the compressor,
        /// CompressionUtil.cpp SerializeCompressorName), the total compressed and uncompressed sizes, each chunk's sizes, then
        /// each chunk compressed. A v1 header names no compressor, so the caller's format for old files applies.
        /// </summary>
        public byte[] CompressedNew(long length, string v1Format)
        {
            const long Tag = 0x9E2A83C1, V2Tag = Tag | (0x22222222L << 32);
            long tag = r.ReadInt64(), chunkSize = r.ReadInt64();
            string format = v1Format;
            if (tag == V2Tag)
            {
                byte k = r.ReadByte();
                format = k switch { 0 => r.ReadFString()?.Value, 1 => "None", 2 => "Oodle", 3 => "Zlib", 4 => "Gzip", 5 => "LZ4", _ => throw new FormatException($"Invalid compressor {k}") };
            }
            else if (tag != Tag) throw new FormatException($"Invalid compressed archive tag {tag:X} at {r.BaseStream.Position - 16}");
            long compressed = r.ReadInt64(), uncompressed = r.ReadInt64();
            if (uncompressed != length) throw new FormatException($"Compressed archive holds {uncompressed} bytes, not {length}");
            if (chunkSize == Tag) chunkSize = 131072;
            if (chunkSize <= 0 || chunkSize >= int.MaxValue) throw new FormatException($"Invalid compression chunk size {chunkSize}");
            long chunks = (uncompressed + chunkSize - 1) / chunkSize;
            var sizes = new (long Compressed, long Uncompressed)[chunks];
            for (long i = 0; i < chunks; i++) sizes[i] = (r.ReadInt64(), r.ReadInt64());
            if (sizes.Sum(x => x.Compressed) != compressed || sizes.Sum(x => x.Uncompressed) != uncompressed) throw new FormatException("Compressed chunk sizes disagree with the totals");
            var output = new byte[uncompressed];
            long at = 0;
            foreach (var (cs, us) in sizes)
            {
                if (cs < 0 || cs > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Compressed chunk of {cs} bytes overruns the data");
                byte[] chunk = r.ReadBytes((int)cs), plain;
                switch (format)
                {
                    case "None": plain = chunk; break;
                    case "Oodle": plain = Oodle.Decompress(chunk, (int)us); break;
                    case "Zlib": case "Gzip":
                    {
                        plain = new byte[us];
                        using Stream z = format == "Zlib" ? new System.IO.Compression.ZLibStream(new MemoryStream(chunk), System.IO.Compression.CompressionMode.Decompress)
                            : new System.IO.Compression.GZipStream(new MemoryStream(chunk), System.IO.Compression.CompressionMode.Decompress);
                        z.ReadExactly(plain);
                        if (z.ReadByte() != -1)
                            r.Asset.Coverage.Report("discarded-decompressed-data", $"{format} chunk produces more than its declared {us} bytes; excess decompressed data is not retained.");
                        break;
                    }
                    default: throw new FormatException($"Unsupported compression format {format}");
                }
                if (plain.Length != us) throw new FormatException($"Chunk decompressed to {plain.Length} of {us} bytes");
                System.Array.Copy(plain, 0, output, at, us);
                at += us;
            }
            return output;
        }
        public byte[] Bytes64()
        {
            long n = r.ReadInt64();
            if (n < 0 || n > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Invalid byte count {n} at {r.BaseStream.Position - 8}");
            return r.ReadBytes((int)n);
        }
        public TextPropertyData Text()
        {
            var t = new TextPropertyData(FName.DefineDummy(r.Asset, "Text"));
            t.Read(r, false, 0);
            return t;
        }
        /// <summary>FStripDataFlags: the global and class strip flags.</summary>
        // FStripDataFlags: the global flags (bit 0 editor data, bit 1 audio-visual data stripped), then the class's own
        public byte[] StripFlags(Dictionary<string, object> d, string key = "StripFlags") { var s = r.ReadBytes(2); d[key] = new Dictionary<string, object> { ["Global"] = s[0], ["Class"] = s[1] }; return s; }
        /// <summary>
        /// TArray::BulkSerialize: the element size, then the elements; an editor save writes them one by one, which for these
        /// types matches their memory layout. <paramref name="elementSize"/> overrides the stored size where per-element
        /// serialization was forced on an older layout.
        /// </summary>
        /// <summary>
        /// A bulk-serialized array decoded by its element layout, one character per field: f float, d double, i int32, u uint32,
        /// h uint16, b uint8. A layout of one repeated field gives a flat array of that type; otherwise each element's fields.
        /// The layout's size must equal the stored element size.
        /// </summary>
        public object Bulk(string layout, int? elementSize = null)
        {
            int stored = r.ReadInt32();
            int size = elementSize ?? stored;
            int width = layout.Sum(f => f switch { 'f' or 'i' or 'u' => 4, 'd' => 8, 'h' => 2, 'b' => 1, _ => throw new ArgumentException(layout) });
            if (size != width) throw new FormatException($"Bulk element of {size} bytes, layout {layout} of {width}");
            int n = Count(size);
            object Field(char f) => f switch { 'f' => r.ReadSingle(), 'd' => r.ReadDouble(), 'i' => r.ReadInt32(), 'u' => r.ReadUInt32(), 'h' => r.ReadUInt16(), _ => r.ReadByte() };
            if (layout.Distinct().Count() == 1)
            {
                switch (layout[0])
                {
                    case 'f': { var a = new float[n * layout.Length]; for (int i = 0; i < a.Length; i++) a[i] = r.ReadSingle(); return a; }
                    case 'd': { var a = new double[n * layout.Length]; for (int i = 0; i < a.Length; i++) a[i] = r.ReadDouble(); return a; }
                    case 'i': { var a = new int[n * layout.Length]; for (int i = 0; i < a.Length; i++) a[i] = r.ReadInt32(); return a; }
                }
            }
            var rows = new List<object[]>(n);
            for (int e = 0; e < n; e++) rows.Add(layout.Select(Field).ToArray());
            return rows;
        }
        public object[] Bulk(int? elementSize = null)
        {
            int stored = r.ReadInt32();
            int size = elementSize ?? stored;
            if (size <= 0) throw new FormatException($"Invalid bulk element size {stored} at {r.BaseStream.Position - 4}");
            int n = Count(size);
            return new object[] { stored, n, r.ReadBytes(checked(n * size)) };
        }
        /// <summary>
        /// BulkData.cpp FBulkData::Serialize and operator&lt;&lt;(FBulkMetaResource): flags, element count, size on disk and
        /// offset (64-bit sizes with BULKDATA_Size64Bit), a legacy version word with BULKDATA_BadDataVersion, the duplicate
        /// payload's meta with BULKDATA_DuplicateNonOptionalPayload, then the payload when it is inline (not at the end of the
        /// file, in a separate file or unused).
        /// </summary>
        public Dictionary<string, object> ByteBulk(string format)
        {
            var bulk = ByteBulk();
            if (bulk["Payload"] is byte[] bytes) bulk["Payload"] = new Payload(format, bytes);
            return bulk;
        }
        public Dictionary<string, object> ByteBulk()
        {
            uint flags = r.ReadUInt32();
            bool wide = (flags & (1u << 13)) != 0;
            long count = wide ? r.ReadInt64() : r.ReadInt32(), size = wide ? r.ReadInt64() : r.ReadInt32(), offset = r.ReadInt64();
            if ((flags & (1u << 15)) != 0)
            {
                r.Asset.Coverage.Report("discarded-value", "Bulk data's legacy bad-data-version field was read without retaining it.");
                r.ReadUInt16();
            }
            object duplicate = null;
            if ((flags & (1u << 14)) != 0) duplicate = new object[] { r.ReadUInt32(), wide ? r.ReadInt64() : r.ReadInt32(), r.ReadInt64() };
            byte[] payload = null;
            if ((flags & (1u | (1u << 8) | (1u << 5))) == 0 && size > 0)
            {
                if (size > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Inline bulk data of {size} bytes overruns the export");
                payload = r.ReadBytes((int)size);
            }
            return new Dictionary<string, object> { ["Flags"] = flags, ["ElementCount"] = count, ["SizeOnDisk"] = size,
                ["OffsetInFile"] = offset, ["DuplicatePayload"] = duplicate, ["Payload"] = payload };
        }
        /// <summary>
        /// EditorBulkData.cpp FEditorBulkData::Serialize: flags, bulk data ID, payload hash (FIoHash) and size, then the payload's
        /// offset in the file unless it is in the package trailer, virtualized or cooked.
        /// </summary>
        public Dictionary<string, object> EditorBulk()
        {
            uint flags = r.ReadUInt32();
            var id = r.ReadGuid();
            var hash = r.ReadBytes(20);
            long size = r.ReadInt64();
            long? offset = (flags & ((1u << 9) | 1u | (1u << 10))) == 0 ? r.ReadInt64() : null;
            return new Dictionary<string, object> { ["Flags"] = flags, ["BulkDataId"] = id, ["PayloadHash"] = Convert.ToHexString(hash),
                ["PayloadSize"] = size, ["PayloadOffset"] = offset };
        }
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
    /// <summary>MorphTarget.cpp operator&lt;&lt;(FArchive&amp;, FMorphTargetLODModel&amp;): the vertex deltas (position delta,
    /// tangent-Z delta and source vertex index, as three parallel arrays), the base mesh's vertex count, the section indices,
    /// whether the engine generated the morph, and its source file.</summary>
    static Dictionary<string, object> ReadMorphTargetLODModel(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        var m = new Dictionary<string, object>();
        int editor = (int)asset.GetCustomVersion<FEditorObjectVersion>();
        int fortnite = (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>();
        void Vertices()
        {
            int n = c.Count(28);
            bool packedTangent = asset.ObjectVersion < ObjectVersion.VER_UE4_MORPHTARGET_CPU_TANGENTZDELTA_FORMATCHANGE;
            var position = new float[n * 3]; var tangent = new float[n * 3]; var source = new uint[n];
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 3; k++) position[i * 3 + k] = r.ReadSingle();
                if (packedTangent)
                {
                    // FDeprecatedSerializedPackedNormal: four bytes, each a component mapped from [0, 255] to [-1, 1]
                    for (int k = 0; k < 3; k++) tangent[i * 3 + k] = r.ReadByte() / 127.5f - 1;
                    asset.Coverage.Report("discarded-value", "MorphTarget packed tangent fourth component was read without retaining it.");
                    r.ReadByte();
                }
                else for (int k = 0; k < 3; k++) tangent[i * 3 + k] = r.ReadSingle();
                source[i] = r.ReadUInt32();
            }
            m["PositionDeltas"] = position; m["TangentZDeltas"] = tangent; m["SourceIndices"] = source;
        }
        if (editor < (int)FEditorObjectVersion.AddedMorphTargetSectionIndices) { Vertices(); m["NumBaseMeshVerts"] = r.ReadInt32(); }
        else if (fortnite < (int)FFortniteMainBranchObjectVersion.SaveGeneratedMorphTargetByEngine)
        {
            Vertices(); m["NumBaseMeshVerts"] = r.ReadInt32(); m["SectionIndices"] = c.Array(() => r.ReadInt32(), 4);
        }
        else
        {
            bool stripped = asset.GetCustomVersion<FUE5SpecialProjectStreamObjectVersion>() >= FUE5SpecialProjectStreamObjectVersion.StripMorphTargetSourceDataForCookedBuilds && c.Bool();
            m["bVerticesAreStrippedForCookedBuilds"] = stripped;
            if (stripped) m["NumVertices"] = r.ReadInt32(); else Vertices();
            m["NumBaseMeshVerts"] = r.ReadInt32();
            m["SectionIndices"] = c.Array(() => r.ReadInt32(), 4);
            m["bGeneratedByEngine"] = c.Bool();
        }
        if (fortnite >= (int)FFortniteMainBranchObjectVersion.MorphTargetCustomImport) m["SourceFilename"] = c.Str();
        return m;
    }

    const int ConvertFPCGMetadataAttributeToGenericAttributes = 266, MergePCGMetadataAttributeBaseAndGeneric = 267;

    /// <summary>PCGMetadataDomain.cpp FPCGMetadataDomain::Serialize: the attribute count, then per attribute its name, its
    /// description (a type ID before MergePCGMetadataAttributeBaseAndGeneric, then FPCGMetadataAttributeDesc's tagged
    /// properties against the default description) and the attribute; then the parent entry keys.</summary>
    static Dictionary<string, object> ReadPCGMetadataDomain(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        int fortnite = (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>();
        var attributes = new List<object>();
        int n = r.ReadInt32();
        for (int i = 0; i < n; i++)
        {
            var a = new Dictionary<string, object> { ["Name"] = c.Name() };
            int type; string typeObject = null; var containers = new List<string>();
            if (fortnite < MergePCGMetadataAttributeBaseAndGeneric) { type = r.ReadInt32(); a["ValueType"] = type; }
            else
            {
                var desc = c.Tagged("PCGMetadataAttributeDesc");
                a["Desc"] = desc;
                (type, typeObject) = PCGDescType(desc, containers);
            }
            ReadPCGMetadataAttribute(a, c, r, asset, type, typeObject, containers, fortnite);
            attributes.Add(a);
        }
        return new Dictionary<string, object> { ["Attributes"] = attributes, ["ParentKeys"] = c.Array(() => r.ReadInt64(), 8) };
    }

    static readonly string[] PCGMetadataTypes = { "Float", "Double", "Integer32", "Integer64", "Vector2", "Vector", "Vector4",
        "Quaternion", "Transform", "String", "Boolean", "Rotator", "Name", "SoftObjectPath", "SoftClassPath", "EndLegacyTypes",
        "Byte", "Text", "Enum", "Struct", "Object", "SoftObject", "Class", "SoftClass" };

    /// <summary>The value type, its object and the container types from a decoded FPCGMetadataAttributeDesc.</summary>
    static (int, string) PCGDescType(StructPropertyData desc, List<string> containers)
    {
        int type = -1; string typeObject = null;
        foreach (var p in desc.Value)
        {
            switch (p.Name.ToString())
            {
                case "ValueType":
                    string v = (p as PropertyTypes.Objects.EnumPropertyData)?.Value?.ToString() ?? (p as PropertyTypes.Objects.BytePropertyData)?.EnumValue?.ToString();
                    if (v != null) type = Array.IndexOf(PCGMetadataTypes, v.Split("::").Last());
                    break;
                case "ValueTypeObject": typeObject = (p as PropertyTypes.Objects.ObjectPropertyData)?.Value?.ToString(); break;
                case "ContainerTypes":
                    if (p is PropertyTypes.Objects.ArrayPropertyData arr)
                        foreach (var e in arr.Value) containers.Add(((e as PropertyTypes.Objects.EnumPropertyData)?.Value?.ToString() ?? (e as PropertyTypes.Objects.BytePropertyData)?.EnumValue?.ToString() ?? "").Split("::").Last());
                    break;
            }
        }
        // FPCGMetadataAttributeDesc's default value type is Unknown (255): an unserialized type is the default
        if (type < 0) throw new FormatException("PCG attribute description without a value type");
        return (type, typeObject);
    }

    /// <summary>PCGMetadataAttribute.cpp FPCGMetadataAttributeBase::BaseSerialize and Serialize: the entry-to-value key map, the
    /// parent attribute ID, name and ID; then (without a parent) the cached description, the default value and the values,
    /// serialized by the property PCG builds from the description. Before ConvertFPCGMetadataAttributeToGenericAttributes the
    /// values and default value followed the base data as a typed array and value.</summary>
    static void ReadPCGMetadataAttribute(Dictionary<string, object> a, Cursor c, AssetBinaryReader r, UAsset asset, int type,
        string typeObject, List<string> containers, int fortnite)
    {
        a["EntryToValueKeyMap"] = c.Array(() => new object[] { r.ReadInt64(), r.ReadInt32() }, 12);
        int parent = r.ReadInt32();
        a["ParentAttributeId"] = parent;
        a["AttributeName"] = c.Name();
        a["AttributeId"] = r.ReadInt32();
        if (fortnite < ConvertFPCGMetadataAttributeToGenericAttributes)
        {
            // TArray<bool> is one of TArray's one-byte element types, serialized as raw bytes; a lone bool is four bytes
            a["Values"] = PCGMetadataTypes[type] == "Boolean" ? c.Array(() => (object)(r.ReadByte() != 0), 1) : c.Array(() => PCGValue(c, r, type, typeObject, true), 1);
            a["DefaultValue"] = PCGValue(c, r, type, typeObject, true);
            return;
        }
        if (parent >= 0) throw new FormatException("PCG attribute with a parent: its description is the parent's, in another domain");
        var desc = c.Tagged("PCGMetadataAttributeDesc");
        a["CachedDesc"] = desc;
        containers = new List<string>();
        (type, typeObject) = PCGDescType(desc, containers);
        if (containers.Count > 0) throw new FormatException($"PCG {string.Join("/", containers)} attribute");
        a["DefaultValue"] = PCGValue(c, r, type, typeObject, false);
        a["Values"] = c.Array(() => PCGValue(c, r, type, typeObject, false), 1);
    }

    /// <summary>One PCG attribute value: by operator&lt;&lt; for the legacy typed arrays (a bool is four bytes), by the
    /// property's SerializeItem otherwise (a bool is one byte). Vectors, rotators, quaternions and transforms are immutable
    /// structs, serialized binary in double precision; a transform is its rotation, translation and scale.</summary>
    static object PCGValue(Cursor c, AssetBinaryReader r, int type, string typeObject, bool legacy)
    {
        switch (PCGMetadataTypes[type])
        {
            case "Float": return r.ReadSingle();
            case "Double": return r.ReadDouble();
            case "Integer32": return r.ReadInt32();
            case "Integer64": return r.ReadInt64();
            case "Vector2": return c.Doubles(2, true);
            case "Vector": case "Rotator": return c.Doubles(3, true);
            case "Vector4": case "Quaternion": return c.Doubles(4, true);
            case "Transform": return c.Doubles(10, true);
            case "String": return c.Str();
            case "Boolean": return legacy ? c.Bool() : r.ReadByte() != 0;
            case "Name": return c.Name();
            case "SoftObjectPath": case "SoftClassPath": case "SoftObject": case "SoftClass": return new FSoftObjectPath(r);
            case "Byte": case "Enum": return r.ReadByte();
            case "Text": return c.Text();
            case "Object": case "Class": return new FPackageIndex(r);
            case "Struct": return c.Tagged(typeObject?.Split('.').Last() ?? "Generic");
            default: throw new FormatException($"PCG attribute type {type}");
        }
    }

}

/// <summary>Bytes whose format is their own rather than a serialized engine structure, named by that format.</summary>
public sealed class Payload
{
    [JsonProperty] public string Format;
    [JsonProperty] public byte[] Data;
    public Payload(string format, byte[] data) { Format = format; Data = data; }
}
