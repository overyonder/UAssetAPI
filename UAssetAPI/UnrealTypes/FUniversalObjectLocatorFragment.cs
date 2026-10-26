using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;

namespace UAssetAPI.UnrealTypes;

public class UniversalObjectLocatorFragmentPropertyData : StructPropertyData
{
    /// <summary>
    /// The fragment types registered with IUniversalObjectLocatorModule::RegisterFragmentType in 5.8.3, by ID, and their
    /// payload structs, all serialized as tagged properties. UniversalObjectLocatorFragment.cpp reads an unregistered ID's
    /// payload as FUniversalObjectLocatorEmptyPayload.
    /// </summary>
    private static readonly Dictionary<string, string> FragmentTypeRegistry = new()
    {
        { "actor", "ActorLocatorFragment" },
        { "asset", "AssetLocatorFragment" },
        { "animinst", "AnimInstanceLocatorFragment" },
        { "ls_lazy_obj_ptr", "LegacyLazyObjectPtrFragment" },
        { "subobj", "SubObjectLocator" },
        { "uobj", "DirectPathObjectLocator" },
        { "animobjfunc", "AnimNextObjectFunctionLocatorFragment" },
        { "animobjprop", "AnimNextObjectPropertyLocatorFragment" },
        { "animobjcast", "AnimNextObjectCastLocatorFragment" },
        { "animcomp", "AnimNextComponentLocatorFragment" },
        { "animactor", "AnimNextActorLocatorFragment" },
        { "usdprim", "UsdPrimLocatorFragment" },
    };

    public FName FragmentTypeID;

    public UniversalObjectLocatorFragmentPropertyData(FName name, FName forcedType) : base(name, forcedType) { }
    public UniversalObjectLocatorFragmentPropertyData(FName name) : base(name) { }
    public UniversalObjectLocatorFragmentPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("UniversalObjectLocatorFragment");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;

    public override void Read(AssetBinaryReader reader, bool includeHeader, long leng1, long leng2 = 0, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        FragmentTypeID = reader.ReadFName();
        if (FragmentTypeID.Value.Value == "None") return;

        StructType = FName.DefineDummy(reader.Asset, FragmentTypeRegistry.TryGetValue(FragmentTypeID.Value.Value, out var structType)
            ? structType : "UniversalObjectLocatorEmptyPayload");

        base.Read(reader, includeHeader, 1, leng2, PropertySerializationContext.StructFallback);
    }

    public override int Write(AssetBinaryWriter writer, bool includeHeader, PropertySerializationContext serializationContext = PropertySerializationContext.Normal)
    {
        writer.Write(FragmentTypeID);
        int res = 8;
        if (FragmentTypeID.Value.Value == "None") return res;
        res += base.Write(writer, includeHeader, PropertySerializationContext.StructFallback);
        return res;
    }
}
