# verdicts.py <inv dir> <UAssetAPI dir> > worklist.tsv: one verdict per work-list item
import sys,re,os,glob
inv,U=sys.argv[1],sys.argv[2]
src=''.join(open(f,errors='ignore').read() for f in glob.glob(U+'/**/*.cs',recursive=True) if '/obj/' not in f and '/bin/' not in f)
cases=set(re.findall(r'case "([UA]\w+)":',open(U+'/ExportTypes/ClassSerializers.cs').read()))
ptypes=set(re.findall(r'new FString\("(\w+)"\)',src))
nothing=set(l.strip() for l in open(inv+'/class-writes-nothing.txt'))|set(l.strip() for l in open(inv+'/struct-writes-nothing.txt'))
flags={'UActorComponent','USceneComponent','UStaticMeshComponent','AActor','UBlueprintCore','UNiagaraDataInterfaceTexture','UWorld','UWorldPartition','UMaterialInstance','UMaterialInterface','UMaterial','UMaterialInterfaceEditorOnlyData','UBodySetup','UObjectRedirector','USkyAtmosphereComponent','UPCGComponent','UStaticMesh','UAnimationAsset','UAnimSequence','UControlRig','URigVMMemoryStorageGeneratorClass','URigVMBlueprintGeneratedClass','UControlRigBlueprintGeneratedClass','UUAFAnimGraph','USoundNode','USoundCue','USoundNodeWavePlayer'}
exports={'UObject':'NormalExport (tagged properties and object GUID)','UClass':'ClassExport','UStruct':'StructExport','UFunction':'FunctionExport','UEnum':'EnumExport','UUserDefinedEnum':'EnumExport','UUserDefinedStruct':'UserDefinedStructExport','UScriptStruct':'ScriptStructExport','UDataTable':'DataTableExport','UStringTable':'StringTableExport','ULevel':'LevelExport','UMetaData':'MetaDataExport','UAssetImportData':'AssetImportDataExport','UField':'FieldExport','UProperty':'PropertyExport','UEdGraphNode':'EdGraphNodeExport','URigHierarchy':'RigHierarchyExport','URigVM':'RigVMExport','UBlueprintGeneratedClass':'ClassExport'}
manual={
'UControlRigBlueprint':'decoded: NormalExport (IControlRigEditorAssetInterface::Serialize writes the tagged properties)',
'UChaosVehicleMovementComponent':'no data: remote object migration only','UWaterBodyComponent':'no data: reference collection only','UNiagaraComponent':'no data',
'UMetaSoundSource':'no data: cooked quality settings only when cooked','UAbilitySystemComponent':'no data: remote object migration only',
'UK2Node':'no data: reference collection only','UK2Node_FunctionEntry':'no data: only without a backing archive','UK2Node_MathExpression':'no data',
'UK2Node_EditablePinBase':'decoded: EdGraphNodeExport (user-defined pins)','UK2Node_DynamicCast':'decoded: EdGraphNodeExport (pure state)',
'UAudioComponent':'no data: reference collection only','UAnimSequenceBase':'no data: curve fix-ups on load','UChildActorComponent':'no data: only when duplicating',
'USkinnedMeshComponent':'no data: remote object migration only','UBoolProperty':'decoded: PropertyExport','UStructProperty':'decoded: PropertyExport',
'UBlueprint':'no data','USkyLightComponent':'legacy: a spherical-harmonics irradiance map between SKYLIGHT_MOBILE_IRRADIANCE_MAP and SkyLightRemoveMobileIrradianceMap, in no reference package',
'ULightComponentBase':'no data: reference collection only','ULightComponent':'legacy: depth maps before MapBuildDataSeparatePackage, in no reference package',
'UPrimitiveComponent':'no data: remote object migration only','UCharacterMovementComponent':'no data: remote object migration only',
'USkeletalMeshComponent':'legacy: a body setup before REMOVE_SKELETALMESH_COMPONENT_BODYSETUP_SERIALIZATION, in no reference package',
'APlayerController':'no data: remote object migration only','UParticleSystemComponent':'no data','UPhysicsConstraintComponent':'no data: reference collection and migration only',
'AWorldDataLayers':'no data: only when duplicating','UDEPRECATED_MetaData':'decoded: MetaDataExport','URigVMLink':'decoded: RigVMLinkExport',
'USplineComponent':'decoded: SceneComponentExport','FNiagaraTypeDefinition':'tagged: writes its tagged properties',
'FRemoteControlProperty':'decoded: RemoteControlPropertyPropertyData (its tagged properties)','FAvaSequenceTime':'decoded: AvaSequenceTimePropertyData (its tagged properties)',
'FTransform':'tagged: WithSerializer is commented out and FTransform is not immutable (Property.cpp, NoExportTypes.h)','FEdGraphPinType':'decoded: EdGraphPin','FRawAnimSequenceTrack':'decoded: RawAnimSequenceTrackPropertyData',
'FShaderValueTypeHandle':'decoded: ShaderValueTypeHandlePropertyData',
'UMetaHumanCharacter':'decoded: ClassSerializers UMetaHumanCharacter',
# classes whose Serialize writes nothing in an editor save, from reading the body
'UFastGeoWorldPartitionRuntimeCellTransformer':'no data: only when duplicating for PIE','USubmixEffectDynamicsProcessorPreset':'no data: migration only',
'UMaterialExpressionSwitch':'no data: versions only','UForceFeedbackComponent':'no data: reference collection only','ULevelInstanceComponent':'no data: reference collection only',
'UMovieSceneVisibilityTrack':'no data: preload only','UInterchangePipelineBase':'no data: only when duplicating','UGroomBindingAsset':'no data: platform data only outside the editor',
'AChaosCacheManager':'no data: PostSerialize migration','UMetaSoundPatch':'no data: FAssetHelper::SerializeToArchive is empty','UMetadataFilterRule':'no data: migration only',
'UEnhancedInputUserSettings':'instance data only: settings objects are saved to save games, and class default objects never run the tail',
'UAnimInstance':'instance data only: anim instances are transient, and class default objects never run the tail',
'UPoseSearchDatabase':'no data: cooked search index only','UGameplayAbility':'no data: remote object migration only','URigVMHost':'no data: reference collection only',
'UHierarchyRoot':'no data','UDataAsset':'no data: flags only','UDistributionFloat':'no data','UDistributionVector':'no data','UNiagaraSpriteRendererProperties':'no data: derived data only when cooked',
'UNiagaraRibbonRendererProperties':'no data','UMaterialExpression':'no data: input reordering on load','UMaterialExpressionBreakMaterialAttributes':'no data','UMaterialExpressionClamp':'no data',
'UMaterialExpressionCustom':'no data: code fix-ups on load','UMaterialExpressionFeatureLevelSwitch':'no data','UMaterialExpressionLandscapeLayerBlend':'no data','UMaterialExpressionMakeMaterialAttributes':'no data',
'UObjectPropertyBase':'decoded: PropertyExport (legacy UObjectProperty)','UArrayProperty':'decoded: PropertyExport','UByteProperty':'decoded: PropertyExport','UClassProperty':'decoded: PropertyExport',
'UDelegateProperty':'decoded: PropertyExport','UEnumProperty':'decoded: PropertyExport','UInterfaceProperty':'decoded: PropertyExport','UMapProperty':'decoded: PropertyExport','UMulticastDelegateProperty':'decoded: PropertyExport',
# structs
'FFrameRate':'tagged: WithSerializer is commented out (Property.cpp)','FFrameTime':'tagged: WithSerializer is commented out (Property.cpp)',
'FStateTreeReference':'tagged: Serialize returns false','FStateTreeStateLink':'tagged: Serialize returns false','FFloatCurve':'tagged: FAnimCurveBase::Serialize returns false',
'FTransformCurve':'tagged: FAnimCurveBase::Serialize returns false','FVectorCurve':'tagged: FAnimCurveBase::Serialize returns false','FAnimCurveBase':'tagged: Serialize returns false',
'FIKRetargetIKChainsOpSettings':'tagged: SerializeOpWithVersion writes the tagged properties','FIKRetargetPelvisMotionOpSettings':'tagged: SerializeOpWithVersion','FIKRetargetPinBoneOpSettings':'tagged: SerializeOpWithVersion',
'FIKRetargetRootMotionOpSettings':'tagged: SerializeOpWithVersion','FIKRetargetStrideWarpingOpSettings':'tagged: SerializeOpWithVersion','FRetargetIKChainSettings':'tagged: writes its tagged properties',
'FAnimSyncMarker':'tagged: writes its tagged properties','FAnimNode_LiveLinkPose':'tagged: writes its tagged properties','FVMExternalFunctionBindingInfo':'tagged: writes its tagged properties',
'FShadingModelMaterialInput':'tagged: no registered struct ops (MaterialShared.cpp)','FSubstrateMaterialInput':'tagged: no registered struct ops (MaterialShared.cpp)',
'FType':'not a struct: macro parameter in StateTreeTypes.h, ChaosVD wrappers','FStructType':'not a struct: macro parameter in UAFBindableTypes.h',
}
rows=[]
def verdict(name, kind):
    if name in manual: return manual[name]
    if name in cases: return 'decoded: ClassSerializers '+name
    if name in flags: return 'decoded: NativeClassTails '+name
    if name in exports: return 'decoded: '+exports[name]
    short=name[1:] if name[:1] in 'UAF' else name
    if kind=='struct' and short in ptypes: return 'decoded: '+short+'PropertyData'
    if name in nothing: return 'no data: the body never writes to the archive'
    return 'UNCLASSIFIED'
for line in open(inv+'/worklist-classes.tsv'):
    f=line.rstrip('\n').split('\t'); rows.append(('class',f[0],f[1],verdict(f[0],'class'),f[3],f[5].split('Engine/')[-1]))
seen={r[1] for r in rows}
for line in open(inv+'/review-classes.tsv'):
    f=line.rstrip('\n').split('\t')
    if f[0] not in seen: rows.append(('class',f[0],f[1],verdict(f[0],'class'),f[3],f[5].split('Engine/')[-1]))
for line in open(inv+'/worklist-structs.tsv'):
    f=line.rstrip('\n').split('\t'); n='F'+f[0]; rows.append(('struct',n,f[1],verdict(n,'struct'),f[2],f[3].split('Engine/')[-1]))
# items the source enumeration missed: macro-declared traits, project serializers, classes only in packages whose headers failed
extra=[('struct','F'+n,'DECLARE_BINDABLE_STRUCT_OPS','decoded: '+n+'PropertyData','advent-test','Plugins/Experimental/UAF/UAF/Source/UAF/Public/BindableValue/UAFBindableTypes.h') for n in
  ['BindableBool','BindableByte','BindableDouble','BindableEnum','BindableFloat','BindableInt32','BindableInt64','BindableName','BindableObject','BindableQuat','BindableStruct','BindableTransform','BindableVector']]
extra+=[('struct','FChaosVD*','CVD_IMPLEMENT_SERIALIZER','unused: Chaos Visual Debugger recording data, in no package','0','Source/Runtime/Experimental/ChaosVisualDebugger'),
  ('class','UPointCloudImpl','CitySample RuleProcessor','decoded: ClassSerializers UPointCloudImpl','34','CitySample Plugins/RuleProcessor/Source/PointCloud/Private/PointCloudImpl.cpp'),
  ('class','UMetadataFilterRule','CitySample RuleProcessor','no data: migration only','14','CitySample Plugins/RuleProcessor/Source/PointCloudEditor/Private/Rules/MetadataFilterRule.cpp'),
  ('struct','FCustomOverrides','CitySample RuleProcessor','unused: in no package','0','CitySample Plugins/RuleProcessor/Source/PointCloud/Public/PointCloudSliceAndDiceCustomOverrides.h'),
  ('struct','FAdventRemnancePartState','advent','unused: save-game state, in no package','0','advent-test Source/advent/Private/Remnance/AdventRemnanceTypes.cpp'),
  ('struct','FAdventCarriedRemnance','advent','unused: save-game state, in no package','0','advent-test Source/advent/Private/Remnance/AdventRemnanceTypes.cpp'),
  ('struct','FAdventSandSavedPatch','AdventSandRuntime','unused: save-game state, in no package','0','advent-test Plugins/AdventSand/Source/AdventSandRuntime/Private/AdventSandState.cpp'),
  ('struct','FAdventSandHeldLoad','AdventSandRuntime','unused: save-game state, in no package','0','advent-test Plugins/AdventSand/Source/AdventSandRuntime/Private/AdventSandState.cpp'),
  ('struct','FEditorOnlyAssetReference','WithSerializer','tagged: Serialize returns false','advent-test','Plugins/MetaHuman/MetaHumanCharacter/Source/MetaHumanCharacterPalette/Private/EditorOnlyAssetReference.cpp'),
  ('class','UMetaHumanCharacter','MetaHumanCharacter','decoded: ClassSerializers UMetaHumanCharacter','advent-test','Plugins/MetaHuman/MetaHumanCharacter/Source/MetaHumanCharacter/Private/MetaHumanCharacter.cpp'),
  ('property','FLazyObjectProperty','SerializeItem','decoded: LazyObjectPropertyData','AutomotiveConfigurator','Source/Runtime/CoreUObject/Private/UObject/PropertyLazyObjectPtr.cpp'),
  ('property','FVerseDynamicProperty','SerializeItem','unused: Verse, in no package','0','Source/Runtime/CoreUObject')]
names={r[1] for r in rows}
rows+=[e for e in extra if e[1] not in names]
print('kind\tname\tmodule or traits\tverdict\tpackages using it\tdefinition')
for r in rows: print('\t'.join(str(x) for x in r))
