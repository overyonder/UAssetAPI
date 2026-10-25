using System.IO;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Export data for a blueprint function.
    /// </summary>
    public class FunctionExport : StructExport
    {
        public EFunctionFlags FunctionFlags;
        /// <summary>Unused replication offset, present only for networked functions.</summary>
        public short RepOffset;
        /// <summary>The event graph function this function calls into, and the offset of that call.</summary>
        public FPackageIndex EventGraphFunction;
        public int EventGraphCallOffset;
        public FunctionExport(Export super) : base(super)
        {
            Asset = super.Asset;
            Extras = super.Extras;
        }

        public FunctionExport(UAsset asset, byte[] extras) : base(asset, extras)
        {

        }

        public FunctionExport()
        {

        }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);
            // Class.cpp UFunction::Serialize: flags, the unused replication offset of networked functions, then the
            // event graph function and call offset.
            FunctionFlags = (EFunctionFlags)reader.ReadUInt32();
            if (FunctionFlags.HasFlag(EFunctionFlags.FUNC_Net)) RepOffset = reader.ReadInt16();
            if (reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_BLUEPRINT_EVENTGRAPH_FASTCALLS_IN_UFUNCTION)
            {
                EventGraphFunction = new FPackageIndex(reader);
                EventGraphCallOffset = reader.ReadInt32();
            }
        }

        public override void Write(AssetBinaryWriter writer)
        {
            base.Write(writer);
            writer.Write((uint)FunctionFlags);
            if (FunctionFlags.HasFlag(EFunctionFlags.FUNC_Net)) writer.Write(RepOffset);
            if (writer.Asset.ObjectVersion >= ObjectVersion.VER_UE4_SERIALIZE_BLUEPRINT_EVENTGRAPH_FASTCALLS_IN_UFUNCTION)
            {
                (EventGraphFunction ?? new FPackageIndex()).Write(writer);
                writer.Write(EventGraphCallOffset);
            }
        }
    }
}
