using Newtonsoft.Json;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// A RigVM model link (Control Rig, UAF and other RigVM graphs). RigVMLink.cpp URigVMLink::Serialize writes only the
/// source and target pin paths, with no tagged properties: it doesn't call UObject::Serialize.
/// </summary>
public class RigVMLinkExport : Export
{
    [JsonProperty] public FString SourcePinPath;
    [JsonProperty] public FString TargetPinPath;

    public RigVMLinkExport(UAsset asset, byte[] extras) : base(asset, extras) { }
    public RigVMLinkExport() { }

    internal static bool Supports(Export export)
    {
        if (export.ClassIndex?.IsImport() != true) return false;
        var cls = export.ClassIndex.ToImport(export.Asset);
        return cls?.ObjectName?.ToString() == "RigVMLink" && cls.OuterIndex?.IsImport() == true &&
            cls.OuterIndex.ToImport(export.Asset)?.ObjectName?.ToString() == "/Script/RigVMDeveloper";
    }

    public override void Read(AssetBinaryReader reader, int nextStarting)
    {
        base.Read(reader, nextStarting);
        SourcePinPath = reader.ReadFString();
        TargetPinPath = reader.ReadFString();
    }

    public override void Write(AssetBinaryWriter writer)
    {
        base.Write(writer);
        writer.Write(SourcePinPath);
        writer.Write(TargetPinPath);
    }
}
