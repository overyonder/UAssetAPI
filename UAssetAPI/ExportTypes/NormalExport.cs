using System;
using System.Collections.Generic;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.ExportTypes
{
    /// <summary>
    /// Enum flags that indicate that additional data may be serialized prior to actual tagged property serialization
    /// Those extensions are used to store additional function to control how TPS will resolved. e.g. use overridable serialization
    /// </summary>
    public enum EClassSerializationControlExtension
    {
        NoExtension = 0x00,
        ReserveForFutureUse = 0x01, // Can be use to add a next group of extension

        ////////////////////////////////////////////////
        // First extension group
        OverridableSerializationInformation = 0x02,

        //
        // Add more extension for the first group here
        //
    }

    /// <summary>
    /// A regular export representing a UObject, with no special serialization.
    /// </summary>
    public class NormalExport : Export
    {
        public List<PropertyData> Data;
        public Guid? ObjectGuid;
        public EClassSerializationControlExtension SerializationControl;
        public EOverriddenPropertyOperation Operation;
        public bool HasLeadingFourNullBytes = false;

        /// <summary>
        /// Native data a class's Serialize writes before calling Super::Serialize, so before the tagged properties: the bytes
        /// up to <see cref="Export.ScriptSerializationStartOffset"/>.
        /// </summary>
        public byte[] LeadingNative;

        /// <summary>What <see cref="LeadingNative"/> decodes to, when its class is known.</summary>
        public object LeadingFields;

        /// <summary>
        /// Whether the class's Serialize never calls UObject::Serialize, so the export has no tagged properties and no object
        /// GUID: everything in it is the class's native data.
        /// </summary>
        public bool HasNoTaggedProperties;

        /// <summary>
        /// For the default object of a Blueprint-generated class: the sparse class data struct that
        /// UBlueprintGeneratedClass::SerializeDefaultObject writes after the tagged properties (null when the class keeps its
        /// archetype's), and that struct's tagged properties, present when the class's sparse class data is serializable.
        /// Null when the export has no such field.
        /// </summary>
        public FPackageIndex SparseClassDataStruct;
        public List<PropertyData> SparseClassData;

        /// <summary>
        /// Gets or sets the value associated with the specified key. This operation loops linearly, so it may not be suitable for high-performance environments.
        /// </summary>
        /// <param name="key">The key associated with the value to get or set.</param>
        public virtual PropertyData this[FName key]
        {
            get
            {
                for (int i = 0; i < Data.Count; i++)
                {
                    if (Data[i].Name == key) return Data[i];
                }
                return null;
            }
            set
            {
                value.Name = key;

                for (int i = 0; i < Data.Count; i++)
                {
                    if (Data[i].Name == key)
                    {
                        Data[i] = value;
                        return;
                    }
                }

                Data.Add(value);
            }
        }

        /// <summary>
        /// Gets or sets the value associated with the specified key. This operation loops linearly, so it may not be suitable for high-performance environments.
        /// </summary>
        /// <param name="key">The key associated with the value to get or set.</param>
        public virtual PropertyData this[string key]
        {
            get
            {
                return this[FName.FromString(Asset, key)];
            }
            set
            {
                this[FName.FromString(Asset, key)] = value;
            }
        }


        /// <summary>
        /// Gets or sets the value at the specified index.
        /// </summary>
        /// <param name="index">The index of the value to get or set.</param>
        public PropertyData this[int index]
        {
            get
            {
                return Data[index];
            }
            set
            {
                Data[index] = value;
            }
        }

        public NormalExport(Export super)
        {
            Asset = super.Asset;
            Extras = super.Extras;
        }

        public NormalExport(UAsset asset, byte[] extras) : base(asset, extras)
        {

        }

        public NormalExport(List<PropertyData> data, UAsset asset, byte[] extras) : base(asset, extras)
        {
            Data = data;
        }

        public NormalExport()
        {

        }

        public override void Read(AssetBinaryReader reader, int nextStarting = 0)
        {
            // 5.4-specific problem; unclear why this occurs. From SCRIPT_SERIALIZATION_OFFSET the export table records where the
            // tagged properties start, so four leading bytes exist only when it says they do; guessing from zero bytes would
            // swallow the start of a first tag whose name is name map entry 0
            if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.SCRIPT_SERIALIZATION_OFFSET && reader.Asset.ObjectVersionUE5 < ObjectVersionUE5.ASSETREGISTRY_PACKAGEBUILDDEPENDENCIES && !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            {
                HasLeadingFourNullBytes = ScriptSerializationStartOffset == 4;
                if (HasLeadingFourNullBytes && reader.ReadInt32() != 0) throw new FormatException("Expected four null bytes before the tagged properties");
            }
            else if (reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.SCRIPT_SERIALIZATION_OFFSET && !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject)
                && ScriptSerializationStartOffset > reader.BaseStream.Position - SerialOffset)
            {
                // a subclass may already have read its own leading data
                LeadingNative = reader.ReadBytes((int)(ScriptSerializationStartOffset - (reader.BaseStream.Position - SerialOffset)));
            }
            else if (reader.Asset.ObjectVersionUE5 > ObjectVersionUE5.DATA_RESOURCES && reader.Asset.ObjectVersionUE5 < ObjectVersionUE5.SCRIPT_SERIALIZATION_OFFSET && !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            {
                int dummy = reader.ReadInt32();
                if (dummy == 0)
                {
                    HasLeadingFourNullBytes = true;
                }
                else
                {
                    HasLeadingFourNullBytes = false;
                    reader.BaseStream.Seek(-4, System.IO.SeekOrigin.Current);
                }
            }

            Data = new List<PropertyData>();
            PropertyData bit;
            HasNoTaggedProperties = !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject) && NativeClassTails.WritesNoTaggedProperties(reader.Asset as UAsset, this);
            if (HasNoTaggedProperties) return;

            var unversionedHeader = new FUnversionedHeader(reader);
            if (!reader.Asset.HasUnversionedProperties && reader.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION)
            {
                SerializationControl = (EClassSerializationControlExtension)reader.ReadByte();

                if (SerializationControl.HasFlag(EClassSerializationControlExtension.OverridableSerializationInformation))
                {
                    Operation = (EOverriddenPropertyOperation)reader.ReadByte();
                }
            }
            FName parentName = GetClassTypeForAncestry(reader.Asset, out FName parentModulePath);
            while (true)
            {
                try { bit = MainSerializer.Read(reader, null, parentName, parentModulePath, unversionedHeader, true); }
                catch (Exception ex) when (Data.Count > 0)
                {
                    // a misread usually belongs to the property before the one that failed, so name it
                    var last = Data[^1];
                    throw new FormatException($"{ex.Message} (after {last.Name} {last.PropertyType} read from {last.Offset})", ex);
                }
                if (bit == null) break;
                Data.Add(bit);
            }

            ObjectGuid = null;
            if (!this.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject) && reader.ReadBooleanInt()) ObjectGuid = reader.ReadGuid();

            if (WritesSparseClassData(reader.Asset as UAsset, out bool? serializable))
            {
                SparseClassDataStruct = new FPackageIndex(reader);
                // a class exported after its default object isn't read yet; the data is then present exactly when bytes remain
                serializable ??= reader.BaseStream.Position < SerialOffset + SerialSize;
                // the data follows only for a struct that overrides the archetype's, in a class that serializes it
                if (!SparseClassDataStruct.IsNull() && serializable.Value)
                {
                    SparseClassData = new List<PropertyData>();
                    FName structName = SparseClassDataStruct.IsExport() ? SparseClassDataStruct.ToExport(reader.Asset).ObjectName : SparseClassDataStruct.ToImport(reader.Asset).ObjectName;
                    // a struct's tagged properties carry no serialization control extension (UStruct::SerializeVersionedTaggedProperties)
                    var header = new FUnversionedHeader(reader);
                    while ((bit = MainSerializer.Read(reader, null, structName, FName.DefineDummy(reader.Asset, "/Script/Engine"), header, true)) != null) SparseClassData.Add(bit);
                }
            }
        }

        /// <summary>
        /// Whether UBlueprintGeneratedClass::SerializeDefaultObject follows this export's tagged properties with a sparse class
        /// data struct: the default object of a class whose own class derives from UBlueprintGeneratedClass, saved at or after
        /// FUE5MainStreamObjectVersion::SparseClassDataStructSerialization. <paramref name="serializable"/> is the class's
        /// bIsSparseClassDataSerializable, which an editor package needs for the data itself; a cooked package always writes it.
        /// </summary>
        bool WritesSparseClassData(UAsset asset, out bool? serializable)
        {
            serializable = false;
            if (asset == null || !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject) || ClassIndex == null || !ClassIndex.IsExport()) return false;
            if (asset.GetCustomVersion(UE5MainStreamGuid) < SparseClassDataStructSerialization) return false;
            var cls = ClassIndex.ToExport(asset);
            if (cls?.ClassIndex == null || !cls.ClassIndex.IsImport()) return false;
            if (!DerivesFromBlueprintGeneratedClass(asset, cls.ClassIndex)) return false;
            if (asset.PackageFlags.HasFlag(EPackageFlags.PKG_Cooked)) serializable = true;
            else if (cls is NormalExport n && n.Data != null) serializable = n.Data.Find(d => d.Name.ToString() == "bIsSparseClassDataSerializable") is BoolPropertyData b && b.Value;
            else serializable = null;
            return true;
        }

        static readonly Guid UE5MainStreamGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
        const int SparseClassDataStructSerialization = 33;

        /// <summary>Engine and engine-plugin classes deriving from UBlueprintGeneratedClass (UE 5.8 source), for packages
        /// that carry no import type hierarchy and are read without mappings.</summary>
        static readonly HashSet<string> EngineBlueprintGeneratedClasses = new()
        {
            "BlueprintGeneratedClass", "ActorModifierCoreGeneratedClass", "AnimBlueprintGeneratedClass",
            "AvaSequenceDirectorGeneratedClass", "ControlRigBlueprintGeneratedClass", "DisplayClusterBlueprintGeneratedClass",
            "MVVMInstancedViewModelGeneratedClass", "MVVMViewModelBlueprintGeneratedClass", "RenderGridBlueprintGeneratedClass",
            "RigVMBlueprintGeneratedClass", "SceneStateGeneratedClass", "SceneStateTaskGeneratedClass",
            "ScriptBlueprintGeneratedClass", "WidgetBlueprintGeneratedClass",
        };

        /// <summary>Walks the class of a class through the package's import type hierarchy, else the mappings' super types,
        /// else the engine's own BlueprintGeneratedClass subclasses.</summary>
        static bool DerivesFromBlueprintGeneratedClass(UAsset asset, FPackageIndex classOfClass)
        {
            string name = classOfClass.ToImport(asset)?.ObjectName?.ToString();
            if (name == null) return false;
            if (EngineBlueprintGeneratedClasses.Contains(name)) return true;
            if (asset.ImportTypeHierarchies != null && asset.ImportTypeHierarchies.TryGetValue(classOfClass, out var hierarchy))
            {
                foreach (var super in hierarchy.SuperTypes) if (super.TypeName.ToString() == "BlueprintGeneratedClass") return true;
                return false;
            }
            for (int depth = 0; depth < 32 && name != null; depth++)
            {
                if (name == "BlueprintGeneratedClass") return true;
                var schema = asset.Mappings?.GetSchemaFromName(name, asset, null, false);
                name = schema?.SuperType;
            }
            if (name != null) asset.Coverage.Report("pruned-depth", "Blueprint generated-class ancestry resolution exceeded 32 parents.");
            return false;
        }

        public override void ResolveAncestries(UAsset asset, AncestryInfo ancestrySoFar)
        {
            var ancestryNew = (AncestryInfo)ancestrySoFar.Clone();
            ancestryNew.SetAsParent(GetClassTypeForAncestry(asset, out FName modulePath), modulePath);

            if (Data != null)
            {
                for (int i = 0; i < Data.Count; i++) Data[i]?.ResolveAncestries(asset, ancestryNew);
            }
            base.ResolveAncestries(asset, ancestrySoFar);
        }

        public override void Write(AssetBinaryWriter writer)
        {
            long exportStart = writer.BaseStream.Position;
            if (HasNoTaggedProperties) return;
            if (LeadingNative != null) writer.Write(LeadingNative);
            // 5.4-specific problem; unclear why this occurs
            if (HasLeadingFourNullBytes && writer.Asset.ObjectVersionUE5 > ObjectVersionUE5.DATA_RESOURCES && writer.Asset.ObjectVersionUE5 < ObjectVersionUE5.ASSETREGISTRY_PACKAGEBUILDDEPENDENCIES && !ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            {
                writer.Write((int)0); // "false" bool?
            }

            FName parentName = GetClassTypeForAncestry(writer.Asset, out FName parentModulePath);

            ScriptSerializationStartOffset = writer.BaseStream.Position - exportStart;
            MainSerializer.GenerateUnversionedHeader(ref Data, parentName, parentModulePath, writer.Asset)?.Write(writer);

            if (!writer.Asset.HasUnversionedProperties && writer.Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION)
            {
                writer.Write((byte)SerializationControl);

                if (SerializationControl.HasFlag(EClassSerializationControlExtension.OverridableSerializationInformation))
                {
                    writer.Write((byte)Operation);
                }
            }

            for (int j = 0; j < Data.Count; j++)
            {
                PropertyData current = Data[j];
                MainSerializer.Write(current, writer, true);
            }
            if (!writer.Asset.HasUnversionedProperties) writer.Write(new FName(writer.Asset, "None"));
            ScriptSerializationEndOffset = writer.BaseStream.Position - exportStart;

            if (this.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))
            {
                if (SparseClassDataStruct == null) return;
                SparseClassDataStruct.Write(writer);
                if (SparseClassData == null) return;
                FName structName = SparseClassDataStruct.IsExport() ? SparseClassDataStruct.ToExport(writer.Asset).ObjectName : SparseClassDataStruct.ToImport(writer.Asset).ObjectName;
                MainSerializer.GenerateUnversionedHeader(ref SparseClassData, structName, FName.DefineDummy(writer.Asset, "/Script/Engine"), writer.Asset)?.Write(writer);
                foreach (var p in SparseClassData) MainSerializer.Write(p, writer, true);
                if (!writer.Asset.HasUnversionedProperties) writer.Write(new FName(writer.Asset, "None"));
                return;
            }
            if (ObjectGuid == null)
            {
                writer.Write((int)0);
            }
            else
            {
                writer.Write((int)1);
                writer.Write((Guid)ObjectGuid);
            }
        }
    }
}
