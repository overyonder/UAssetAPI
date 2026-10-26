using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// An export of UAssetImportData or a subclass, or of UFbxSceneImportData: AssetImportData.cpp and FbxSceneImportData.cpp
    /// write the source description as JSON before Super::Serialize, so before the tagged properties (UAssetImportData from
    /// VER_UE4_ASSET_IMPORT_DATA_AS_JSON), and only with editor data.
    /// </summary>
    public class AssetImportDataExport : NormalExport
    {
        public FString Json;

        public AssetImportDataExport(Export super) : base(super)
        {

        }

        public AssetImportDataExport(UAsset asset, byte[] extras) : base(asset, extras)
        {

        }

        public AssetImportDataExport()
        {

        }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            bool writesJson = !reader.Asset.IsFilterEditorOnly && !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject)
                && (reader.Asset.ObjectVersion >= ObjectVersion.VER_UE4_ASSET_IMPORT_DATA_AS_JSON || NativeClassTails.IsFbxSceneImportData(reader.Asset as UAsset, this));
            if (writesJson) Json = reader.ReadFString();

            base.Read(reader, nextStarting);
        }

        public override void Write(AssetBinaryWriter writer)
        {
            long exportStart = writer.BaseStream.Position;
            if (Json != null) writer.Write(Json);
            long prefix = writer.BaseStream.Position - exportStart;

            base.Write(writer);
            // Script offsets are relative to the export start, which precedes the native Json prefix.
            ScriptSerializationStartOffset += prefix;
            ScriptSerializationEndOffset += prefix;
        }
    }
}
