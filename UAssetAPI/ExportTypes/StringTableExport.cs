using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using UAssetAPI.UnrealTypes;
using UAssetAPI.ExportTypes;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// A string table. Holds Key->SourceString pairs of text.
    /// </summary>
    public class FStringTable : TMap<FString, FString>
    {
        [JsonProperty]
        public FString TableNamespace;

        public FStringTable(FString tableNamespace) : base()
        {
            TableNamespace = tableNamespace;
        }

        public FStringTable() : base()
        {

        }
    }

    /// <summary>
    /// Export data for a string table. See <see cref="FStringTable"/>.
    /// </summary>
    public class StringTableExport : NormalExport
    {
        [JsonProperty]
        public FStringTable Table;

        /// <summary>StringTableCore.cpp: each entry's developer notes, saved with editor data from FFortniteMainBranchObjectVersion::AddDevNotesToFText.</summary>
        [JsonProperty]
        public List<FString> DevNotes;

        /// <summary>StringTableCore.cpp: the meta-data by key, each a map of meta-data name to value.</summary>
        [JsonProperty]
        public List<KeyValuePair<FString, List<KeyValuePair<FName, FString>>>> MetaData;

        public StringTableExport(Export super) : base(super)
        {

        }

        public StringTableExport(FStringTable data, UAsset asset, byte[] extras) : base(asset, extras)
        {
            Table = data;
        }

        public StringTableExport()
        {

        }

        public override void Read(AssetBinaryReader reader, int nextStarting)
        {
            base.Read(reader, nextStarting);

            Table = new FStringTable(reader.ReadFString());
            bool devNotes = !reader.Asset.IsFilterEditorOnly && reader.Asset.GetCustomVersion(FortniteMainGuid) >= AddDevNotesToFText;
            if (devNotes) DevNotes = new List<FString>();

            int numEntries = reader.ReadInt32();
            for (int i = 0; i < numEntries; i++)
            {
                Table.Add(reader.ReadFString(), reader.ReadFString());
                if (devNotes) DevNotes.Add(reader.ReadFString());
            }

            int numMetaData = reader.ReadInt32();
            MetaData = new List<KeyValuePair<FString, List<KeyValuePair<FName, FString>>>>(numMetaData);
            for (int i = 0; i < numMetaData; i++)
            {
                var key = reader.ReadFString();
                int n = reader.ReadInt32();
                var map = new List<KeyValuePair<FName, FString>>(n);
                for (int j = 0; j < n; j++) map.Add(new(reader.ReadFName(), reader.ReadFString()));
                MetaData.Add(new(key, map));
            }
        }

        static readonly Guid FortniteMainGuid = UAPUtils.GUID(0x601D1886, 0xAC644F84, 0xAA16D3DE, 0x0DEAC7D6);
        const int AddDevNotesToFText = 260;

        public override void Write(AssetBinaryWriter writer)
        {
            base.Write(writer);

            writer.Write(Table.TableNamespace);
            writer.Write(Table.Count);
            for (int i = 0; i < Table.Count; i++)
            {
                writer.Write(Table.Keys.ElementAt(i));
                writer.Write(Table[i]);
                if (DevNotes != null) writer.Write(DevNotes[i]);
            }
            if (MetaData == null) return;
            writer.Write(MetaData.Count);
            foreach (var entry in MetaData)
            {
                writer.Write(entry.Key);
                writer.Write(entry.Value.Count);
                foreach (var kv in entry.Value) { writer.Write(kv.Key); writer.Write(kv.Value); }
            }
        }
    }
}
