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
                        if (cooked) { d["CookedShaderMaps"] = Rest(); break; }
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
                    d["DNA"] = Rest();
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
                case "USkeletalMesh": ReadSkeletalMesh(d, c, r, asset); break;
                case "UUAFSystem":
                    // AnimNextModule.cpp: an older module's shared archive buffer, which the engine skips by seeking to the export's end
                    if (Ver(FortniteMainGuid) < AnimNextModuleRefactor) d["SkippedLegacyBuffer"] = Rest();
                    break;

                case "UTexture":
                {
                    // Texture.cpp: strip flags, then with editor data the source's bulk data, an FEditorBulkData; before
                    // FUE5MainStreamObjectVersion::TextureSourceVirtualization a legacy FByteBulkData
                    var strip = c.StripFlags(d);
                    if ((strip[0] & 1) == 0) d["Source"] = Ver(UE5MainGuid) < 42 ? c.ByteBulk() : c.EditorBulk();
                    break;
                }
                case "UTexture2D":
                {
                    // Texture2D.cpp: strip flags and the cooked flag; cooked, whether mips follow and the platform data
                    c.StripFlags(d, "Texture2DStripFlags");
                    bool cooked = c.Bool();
                    d["bCooked"] = cooked;
                    if (cooked) { d["bSerializeMipData"] = c.Bool(); d["CookedPlatformData"] = Rest(); }
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
                    d["DatabaseSize"] = r.ReadInt64();
                    var rest = Rest();
                    if (rest.Length < 20) throw new FormatException("Point cloud database without its digest");
                    d["CompressedDatabase"] = rest[..^20];
                    d["WholeDbHash"] = rest[^20..];
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
                            d["FontData"] = c.Bytes();
                            if (Ver(FortniteMainGuid) >= 180) d["PreprocessedFontGeometries"] = Rest();
                        }
                    }
                    break;
                }
                case "UVectorFieldStatic":
                    // VectorField.cpp: SourceData, an FByteBulkData
                    d["SourceData"] = c.ByteBulk();
                    break;
                case "USubUVAnimation":
                    Cooked(d, c, Rest);
                    break;
                case "UCurveTable": ReadCurveTable(d, c, r, asset); break;
                case "UNNEModelData": ReadNNEModelData(d, c, r, asset); break;
                case "UInterchangeAssetImportData":
                    // InterchangeAssetImportData.cpp: from SerializedInterchangeObjectStoring the node container (a TArray64<uint8>
                    // of its serialized form) and the pipelines (class path and JSON pairs)
                    if (Ver(InterchangeGuid) >= 1) { d["CachedNodeContainer"] = c.Bytes64(); d["CachedPipelines"] = c.Array(() => new object[] { c.Str(), c.Str() }, 8); }
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
                    if (Ver(FortniteMainGuid) >= 211) d["bCooked"] = c.Bool();
                    d["MorphLODModels"] = Rest();
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
                    d["Mesh"] = Rest();
                    break;
                case "UMeshPartitionCollisionComponent":
                {
                    // MeshPartitionCollisionComponent.cpp: with editor data, whether collision data follows, whether it has a mesh,
                    // and the mesh, geometry and the export's last field
                    bool has = c.Bool();
                    d["bHasCollisionData"] = has;
                    if (has) { bool mesh = c.Bool(); d["bHasMesh"] = mesh; if (mesh) d["Mesh"] = Rest(); }
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
                    d["Chunks"] = c.Array(() => new object[] { c.ByteBulk(), r.ReadInt32(), r.ReadSingle(), r.ReadSingle() }, 24);
                    d["Samples"] = c.Array(() => new object[] { r.ReadSingle(), c.Box(), r.ReadInt32(), r.ReadInt32() }, 20);
                    d["VisibilitySamples"] = c.Array(() => new object[] { r.ReadByte(), r.ReadSingle(), r.ReadByte(), r.ReadSingle(), c.Bool() }, 14);
                    break;
                case "UNiagaraSimCache":
                    // NiagaraSimCache.cpp: from SimCache_BulkDataVersion1 each frame's system and emitter buffers, the rest of the export
                    if (Ver(NiagaraGuid) >= 84) d["CacheFrameBuffers"] = Rest();
                    break;
                case "UPCGLandscapeCache":
                    // PCGLandscapeCache.cpp: the cache entries, each its landscape GUID and coordinate, then
                    // FPCGLandscapeCacheEntry::Serialize: point half size, stride, layer names and the bulk data
                    d["CachedData"] = c.Array(() => new object[] { r.ReadGuid(), r.ReadInt32(), r.ReadInt32(), c.Vector(), r.ReadInt32(),
                        c.Array(c.Name, 8), c.ByteBulk() }, 40);
                    break;
                case "UPCGMetadata":
                    // PCGMetadata.cpp: from MultiLevelMetadata the default domain and the domain IDs, then the domains' attributes;
                    // before it the default domain's attributes. The attributes are generated output, the rest of the export.
                    if (Ver(PCGGuid) >= 24)
                    {
                        d["DefaultDomain"] = new object[] { r.ReadByte(), r.ReadInt32() };
                        d["DomainIDs"] = c.Array(() => new object[] { r.ReadByte(), r.ReadInt32() }, 5);
                    }
                    d["Domains"] = Rest();
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
                        if (asset.ObjectVersion >= ObjectVersion.VER_UE4_ATMOSPHERIC_FOG_CACHE_DATA) { d["Transmittance"] = c.ByteBulk(); d["Irradiance"] = c.ByteBulk(); }
                        d["Inscatter"] = c.ByteBulk();
                        d["Counter"] = r.ReadInt32();
                    }
                    break;
                case "UReflectionCaptureComponent": ReadReflectionCaptureLegacy(d, c, r, asset, fileOffset); break;
                case "UInstancedStaticMeshComponent": ReadInstancedStaticMesh(d, c, r, asset, Rest); break;
                case "UHierarchicalInstancedStaticMeshComponent":
                    // HierarchicalInstancedStaticMesh.cpp: the cluster tree, bulk serialized
                    d["ClusterTree"] = c.Bulk();
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
                    d["RecastData"] = r.ReadBytes((int)(sizePos + size - r.BaseStream.Position));
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
                    // MapBuildData.cpp: strip flags, then the baked lighting, reflection capture and sky build data, the rest of
                    // the export
                    c.StripFlags(d);
                    d["BuildData"] = Rest();
                    break;
                case "ULandscapeComponent": ReadLandscapeComponent(d, c, r, asset, export); break;
                case "ULandscapeHeightfieldCollisionComponent":
                    // LandscapeCollision.cpp: before LANDSCAPE_COLLISION_DATA_COOKING the height and dominant layer bulk data;
                    // after it the cooked flag, then cooked collision data or, with editor data, those two and, from
                    // LandscapePhysicalMaterialRenderData, the physical material render data
                    if (asset.ObjectVersion < ObjectVersion.VER_UE4_LANDSCAPE_COLLISION_DATA_COOKING) { d["CollisionHeightData"] = c.ByteBulk(); d["DominantLayerData"] = c.ByteBulk(); break; }
                    {
                        bool cooked = c.Bool();
                        d["bCooked"] = cooked;
                        if (cooked) { d["CookedCollisionData"] = c.Bulk(); break; }
                        d["CollisionHeightData"] = c.ByteBulk(); d["DominantLayerData"] = c.ByteBulk();
                        if (Ver(FortniteMainGuid) >= 37) d["PhysicalMaterialRenderData"] = c.ByteBulk();
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

    /// <summary>
    /// Chaos Field/FieldSystemNodes.cpp: the node FieldNodeFactory makes for a field type and serialization type, then its
    /// Serialize: the name (FFieldNodeBase), then its own fields; child nodes (SerializeInternal) are their field type,
    /// serialization type and node, enums a byte.
    /// </summary>
    static object FieldNode(Cursor c, AssetBinaryReader r, byte fieldType, byte type, int depth)
    {
        if (type == 0) return null;
        if (depth > 64) throw new FormatException("Field node tree nested too deeply");
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
        d["RawData"] = asset.GetCustomVersion(UE5MainGuid) < 66 ? c.ByteBulk() : c.EditorBulk();
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
        d["FileData"] = v >= 4 ? c.Bytes64() : c.Bytes();
        if (v >= 3) d["AdditionalFileData"] = c.Array(() => new object[] { c.Str(), v >= 4 ? c.Bytes64() : c.Bytes() }, 8);
        if (v >= 5) d["RuntimeSettings"] = c.Array(() => new object[] { c.Str(), c.Bytes64() }, 12);
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
    static object ReadAttributeStorage(Cursor c, AssetBinaryReader r, UAsset asset)
    {
        bool multiple = asset.GetCustomVersion(InterchangeGuid) >= 3;
        return new object[] { r.ReadUInt64(), r.ReadSingle(),
            c.Array(() => new object[] { c.Str(),
                multiple ? c.Array(() => r.ReadUInt64(), 8) : new List<ulong> { r.ReadUInt64() },
                multiple ? c.Array(() => r.ReadUInt64(), 8) : new List<ulong> { r.ReadUInt64() },
                r.ReadInt32(), r.ReadUInt32(), r.ReadGuid() }, 36),
            c.Bytes64() };
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
    static object ReadMeshDescriptionBulkData(Cursor c, AssetBinaryReader r, UAsset asset)
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
        object HairDescription() => asset.GetCustomVersion(UE5MainGuid) < 92 ? new object[] { c.ByteBulk(), r.ReadGuid() } : c.EditorBulk();
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
    static void ReadGeometryCollection(Dictionary<string, object> d, Cursor c, AssetBinaryReader r, UAsset asset, Func<byte[]> rest)
    {
        int destruction = asset.GetCustomVersion(DestructionGuid), main = asset.GetCustomVersion(UE5MainGuid);
        if (destruction < 4) throw new FormatException("Geometry collection saved before GeometryCollectionInDDCAndAsset");
        if (main == 1 || (main >= 11 && main < 14)) throw new FormatException("Geometry collection with legacy Nanite data");
        d["bIsCookedOrCooking"] = c.Bool();
        var all = rest();
        if (main < 14) { d["GeometryCollection"] = all; return; }
        if (all.Length < 4 || BitConverter.ToUInt32(all, all.Length - 4) != 0) throw new FormatException("Geometry collection with cooked render data");
        d["GeometryCollection"] = all[..^4];
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
            d["SkippedData"] = r.ReadBytes((int)(end - r.BaseStream.Position));
            return;
        }
        bool valid = c.Bool();
        d["bValid"] = valid;
        if (!valid) return;
        if (asset.GetCustomVersion(RenderingGuid) >= 2) d["CubemapSize"] = r.ReadInt32();
        d["CompressedCapturedData"] = c.Bytes();
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
            d["PerInstanceSMData"] = c.Bulk(asset.ObjectVersionUE5 < ObjectVersionUE5.LARGE_WORLD_COORDINATES ? 64 : null);
            if (asset.GetCustomVersion(RenderingGuid) >= 40) d["PerInstanceSMCustomData"] = c.Bulk();
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
            if (process) d["CookedFormatData"] = c.Array(() => new object[] { c.Name(), c.ByteBulk() }, 24);
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
        d["Vectors"] = c.Bulk(); d["Points"] = c.Bulk(); d["Nodes"] = c.Bulk();
        d["Surfs"] = c.Array(() => new object[] { new FPackageIndex(r), r.ReadUInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(), r.ReadInt32(),
            r.ReadInt32(), new FPackageIndex(r), c.Floats(4), r.ReadSingle(), r.ReadInt32() }, 56);
        d["Verts"] = c.Bulk();
        d["NumSharedSides"] = r.ReadInt32();
        if (!asset.IsFilterEditorOnly || asset.ObjectVersion < ObjectVersion.VER_UE4_REMOVE_UNUSED_UPOLYS_FROM_UMODEL)
        {
            d["Polys"] = new FPackageIndex(r); d["LeafHulls"] = c.Bulk(); d["Leaves"] = c.Bulk();
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
            if (fn < 54) g["DeprecatedWeightData"] = c.Array(() => new object[] { new FPackageIndex(r), c.Bytes() }, 8);
            else
            {
                g["NumElements"] = r.ReadInt32();
                g["WeightOffsets"] = c.Array(() => new object[] { fn < 253 ? new FPackageIndex(r) : c.Name(), r.ReadInt32() }, 8);
                g["HeightWeightData"] = c.Bytes();
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
        public byte[] StripFlags(Dictionary<string, object> d, string key = "StripFlags") { var s = r.ReadBytes(2); d[key] = s; return s; }
        /// <summary>
        /// TArray::BulkSerialize: the element size, then the elements; an editor save writes them one by one, which for these
        /// types matches their memory layout. <paramref name="elementSize"/> overrides the stored size where per-element
        /// serialization was forced on an older layout.
        /// </summary>
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
        public object[] ByteBulk()
        {
            uint flags = r.ReadUInt32();
            bool wide = (flags & (1u << 13)) != 0;
            long count = wide ? r.ReadInt64() : r.ReadInt32(), size = wide ? r.ReadInt64() : r.ReadInt32(), offset = r.ReadInt64();
            if ((flags & (1u << 15)) != 0) r.ReadUInt16();
            object duplicate = null;
            if ((flags & (1u << 14)) != 0) duplicate = new object[] { r.ReadUInt32(), wide ? r.ReadInt64() : r.ReadInt32(), r.ReadInt64() };
            byte[] payload = null;
            if ((flags & (1u | (1u << 8) | (1u << 5))) == 0 && size > 0)
            {
                if (size > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Inline bulk data of {size} bytes overruns the export");
                payload = r.ReadBytes((int)size);
            }
            return new object[] { flags, count, size, offset, duplicate, payload };
        }
        /// <summary>
        /// EditorBulkData.cpp FEditorBulkData::Serialize: flags, bulk data ID, payload hash (FIoHash) and size, then the payload's
        /// offset in the file unless it is in the package trailer, virtualized or cooked.
        /// </summary>
        public object[] EditorBulk()
        {
            uint flags = r.ReadUInt32();
            var id = r.ReadGuid();
            var hash = r.ReadBytes(20);
            long size = r.ReadInt64();
            long? offset = (flags & ((1u << 9) | 1u | (1u << 10))) == 0 ? r.ReadInt64() : null;
            return new object[] { flags, id, hash, size, offset };
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
}
