using UAssetAPI.UnrealTypes;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>RigVMCore/RigVMPropertyBag.cpp FRigVMPropertyBag::Serialize: only its FInstancedPropertyBag.</summary>
public class RigVMPropertyBagPropertyData : InstancedPropertyBagPropertyData
{
    public RigVMPropertyBagPropertyData(FName name) : base(name) { }
    public RigVMPropertyBagPropertyData() { }

    private static readonly FString CurrentPropertyType = new FString("RigVMPropertyBag");
    public override bool HasCustomStructSerialization => true;
    public override FString PropertyType => CurrentPropertyType;
}
