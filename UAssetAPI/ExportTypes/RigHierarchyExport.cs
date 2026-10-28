using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>An element of a rig hierarchy: its key and, in load order, every value its loaders read.</summary>
public class RigHierarchyElement
{
    [JsonProperty] public string Type;
    [JsonProperty] public string Name;
    [JsonProperty] public List<KeyValuePair<string, object>> Fields = new();
}

/// <summary>A rig hierarchy component: its script struct, its key, and the payload its own loader reads after the key.</summary>
public class RigHierarchyComponent
{
    [JsonProperty] public string ScriptStruct;
    [JsonProperty] public string Name;
    [JsonProperty] public string ElementType;
    [JsonProperty] public string ElementName;
    [JsonProperty] public byte[] Payload;
}

/// <summary>
/// ControlRig Rigs/RigHierarchy.cpp URigHierarchy::Load, which reads the whole export (URigHierarchy::Serialize calls no
/// base serializer): the serialization settings, an element stream that from RigHierarchyCompressElements may be Oodle
/// compressed with its own name table, every element's static data and then its inter-element data, the previous name
/// and parent maps, element metadata and components. The export keeps its bytes for writing; the parse is a view of them.
/// </summary>
public class RigHierarchyExport : CustomSerializedExport
{
    static readonly Guid ControlRigGuid = UAPUtils.GUID(0xA7820CFB, 0x20A74359, 0x8C542C14, 0x9623CF50);

    // FControlRigObjectVersion
    const int RigHierarchyMultiParentConstraints = 8, RigHierarchyControlSpaceFavorites = 9, StorageMinMaxValuesAsFloatStorage = 10,
        PerChannelLimits = 13, RemovedMultiParentParentCache = 14, CurveElementValueStateFlag = 16, ControlAnimationType = 17,
        PreferredEulerAnglesForControls = 19, HierarchyElementMetadata = 20, RestrictSpaceSwitchingForControls = 22,
        ControlTransformChannelFiltering = 23, RigHierarchyStoringPreviousNames = 25, RigHierarchyControlPreferredRotationOrder = 26,
        RigHierarchyControlPreferredRotationOrderFlag = 27, RigHierarchyStoresElementMetadata = 28, ConnectorsWithType = 29,
        RigHierarchyIndirectElementStorage = 32, RigHierarchyCompressElements = 33, RigHierarchyStoresComponents = 34,
        RigHierarchyCompactTransformSerialization = 35, RigHierarchyArrayConnectors = 36, RigHierarchyParentContraintWithLabel = 37,
        RigHierarchyPreviousNameAndParentMapUsingHierarchyKey = 38, RigHierarchyPostConstructionConnectors = 39;

    static readonly string[] MetadataTypes = { "Bool", "BoolArray", "Float", "FloatArray", "Int32", "Int32Array", "Name", "NameArray",
        "Vector", "VectorArray", "Rotator", "RotatorArray", "Quat", "QuatArray", "Transform", "TransformArray", "LinearColor",
        "LinearColorArray", "RigElementKey", "RigElementKeyArray" };

    [JsonProperty] public Dictionary<string, object> Settings = new();
    [JsonProperty] public List<RigHierarchyElement> Elements;
    [JsonProperty] public List<KeyValuePair<object, object>> PreviousNameMap;
    [JsonProperty] public List<KeyValuePair<object, object>> PreviousParentMap;
    [JsonProperty] public List<KeyValuePair<object, List<KeyValuePair<string, object>>>> Metadata;
    [JsonProperty] public List<RigHierarchyComponent> Components;
    /// <summary>Why the parse stopped, when it did; the export's bytes are unaffected.</summary>
    [JsonProperty] public string ParseFailure;

    /// <summary>Whether the parse read every byte of the export and of its element stream.</summary>
    public bool FullyParsed => ParseFailure == null;

    public RigHierarchyExport() { }

    // The element stream: the export itself, or the decompressed buffer whose names index UniqueNames.
    sealed class Stream
    {
        public AssetBinaryReader R;
        public List<FName> Names;
        public bool LargeWorld;
        public int CR;

        public int Int() => R.ReadInt32();
        public float Float() => R.ReadSingle();
        public byte Byte() => R.ReadByte();
        public bool Bool()
        {
            uint v = R.ReadUInt32();
            if (v > 1) throw new FormatException($"Invalid bool {v} at {R.BaseStream.Position - 4}");
            return v == 1;
        }
        public string Str() => R.ReadString();
        public string Name()
        {
            if (Names == null) return R.ReadFName().ToString();
            int i = R.ReadInt32();
            if (i < 0 || i >= Names.Count) throw new FormatException($"Invalid rig name index {i}");
            return Names[i].ToString();
        }
        public double Real() => LargeWorld ? R.ReadDouble() : R.ReadSingle();
        public double[] Reals(int n) { var v = new double[n]; for (int i = 0; i < n; i++) v[i] = Real(); return v; }
        public float[] Floats(int n) { var v = new float[n]; for (int i = 0; i < n; i++) v[i] = R.ReadSingle(); return v; }
        /// <summary>FTransform: rotation (quaternion), translation, scale.</summary>
        public double[] Transform() => Reals(10);
        public int Count(int minBytes)
        {
            int n = R.ReadInt32();
            if (n < 0 || (long)n * minBytes > R.BaseStream.Length - R.BaseStream.Position) throw new FormatException($"Invalid count {n} at {R.BaseStream.Position - 4}");
            return n;
        }
        public List<T> Array<T>(Func<T> read, int minBytes) { int n = Count(minBytes); var l = new List<T>(n); for (int i = 0; i < n; i++) l.Add(read()); return l; }
        /// <summary>FRigElementKey::Load: the type's enum name, then the element name.</summary>
        public string[] Key()
        {
            // UEnum::GetValueByName accepts the qualified "ERigElementType::Bone" form that is stored
            string type = Name();
            if (type.StartsWith("ERigElementType::")) type = type.Substring("ERigElementType::".Length);
            return new[] { type, Name() };
        }
        /// <summary>FRigHierarchyKey::Serialize: an optional element key and an optional component key.</summary>
        public object HierarchyKey()
        {
            var k = new Dictionary<string, object>();
            if (Bool()) k["Element"] = Key();
            if (Bool()) k["Component"] = new object[] { Name(), Key() };
            return k;
        }
    }

    public override void Read(AssetBinaryReader reader, int nextStarting = 0)
    {
        base.Read(reader, nextStarting);
        try { Parse(reader.Asset); }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException or DllNotFoundException or EntryPointNotFoundException)
        {
            ParseFailure = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    void Parse(UAsset asset)
    {
        var outer = new Stream
        {
            R = new AssetBinaryReader(new MemoryStream(Data), asset),
            LargeWorld = asset.ObjectVersionUE5 >= ObjectVersionUE5.LARGE_WORLD_COORDINATES,
            CR = asset.GetCustomVersion(ControlRigGuid)
        };
        // FRigHierarchySerializationSettings: by default every transform is stored, uncompressed, in its full form
        int settingsVersion = outer.CR;
        bool compressed = false, local = true, global = true, initial = true, current = true;
        if (outer.CR >= RigHierarchyCompactTransformSerialization)
        {
            settingsVersion = outer.Int();
            Settings["IsSerializingToPackage"] = outer.Bool();
            compressed = outer.Bool();
            Settings["StoreCompactTransforms"] = outer.Bool();
            local = outer.Bool(); global = outer.Bool(); initial = outer.Bool(); current = outer.Bool();
            Settings["SerializationPhase"] = outer.Int();
        }
        else if (outer.CR >= RigHierarchyCompressElements) compressed = outer.Bool();
        Settings["ControlRigVersion"] = settingsVersion;
        Settings["UseCompressedArchive"] = compressed;
        Settings["SerializeLocal"] = local; Settings["SerializeGlobal"] = global; Settings["SerializeInitial"] = initial; Settings["SerializeCurrent"] = current;

        var s = outer;
        if (compressed)
        {
            var names = outer.Array(() => outer.R.ReadFName(), 8);
            int uncompressedSize = outer.Int();
            bool storedCompressed = outer.Bool();
            int n = outer.Count(1);
            var bytes = outer.R.ReadBytes(n);
            Settings["UniqueNames"] = names.Count;
            Settings["StoredCompressed"] = storedCompressed;
            if (outer.R.BaseStream.Position != outer.R.BaseStream.Length) throw new FormatException($"{outer.R.BaseStream.Length - outer.R.BaseStream.Position} bytes after the compressed element stream");
            var buffer = storedCompressed ? Oodle.Decompress(bytes, uncompressedSize) : bytes;
            // the memory reader the engine loads with is at the current package version, so its reals are doubles
            s = new Stream { R = new AssetBinaryReader(new MemoryStream(buffer), asset), Names = names, LargeWorld = true, CR = outer.CR };
        }

        int count = s.Count(16);
        Elements = new List<RigHierarchyElement>(count);
        bool perElement = s.CR < RigHierarchyIndirectElementStorage;
        for (int i = 0; i < count; i++)
        {
            var key = s.Key();
            var e = new RigHierarchyElement { Type = key[0], Name = key[1] };
            Elements.Add(e);
            if (perElement) LoadStatic(s, e, settingsVersion, local, global, initial, current);
        }
        if (!perElement) foreach (var e in Elements) LoadStatic(s, e, settingsVersion, local, global, initial, current);
        foreach (var e in Elements) LoadInter(s, e);

        if (s.CR >= RigHierarchyStoringPreviousNames)
        {
            Func<object> key = s.CR >= RigHierarchyPreviousNameAndParentMapUsingHierarchyKey ? s.HierarchyKey : () => s.Key();
            PreviousNameMap = s.Array(() => new KeyValuePair<object, object>(key(), key()), 16);
            PreviousParentMap = s.Array(() => new KeyValuePair<object, object>(key(), key()), 16);
        }
        if (s.CR >= RigHierarchyStoresElementMetadata)
            Metadata = s.Array(() => new KeyValuePair<object, List<KeyValuePair<string, object>>>(s.Key(), ReadMetadataStorage(s)), 20);
        if (s.CR >= RigHierarchyStoresComponents)
        {
            int components = s.Count(12);
            Components = new List<RigHierarchyComponent>(components);
            if (components > 0)
            {
                var structs = s.Array(s.Str, 4);
                for (int c = 0; c < components; c++)
                {
                    int structIndex = s.Int();
                    long end = s.R.ReadInt64();
                    if (structIndex < 0 || structIndex >= structs.Count || end < s.R.BaseStream.Position || end > s.R.BaseStream.Length) throw new FormatException($"Invalid component {c}");
                    // FRigBaseComponent::Load reads the component key; the type's own loader reads up to the stored end
                    var component = new RigHierarchyComponent { ScriptStruct = structs[structIndex], Name = s.Name() };
                    var owner = s.Key();
                    component.ElementType = owner[0]; component.ElementName = owner[1];
                    component.Payload = s.R.ReadBytes(checked((int)(end - s.R.BaseStream.Position)));
                    Components.Add(component);
                }
            }
        }
        if (s.R.BaseStream.Position != s.R.BaseStream.Length) throw new FormatException($"{s.R.BaseStream.Length - s.R.BaseStream.Position} bytes after the element stream");
        if (!compressed && outer.R.BaseStream.Position != outer.R.BaseStream.Length) throw new FormatException("Bytes after the hierarchy");
    }

    static bool IsTransform(string type) => type is "Bone" or "Null" or "Control" or "Reference" or "Socket";
    static bool IsSingleParent(string type) => type is "Bone" or "Reference" or "Socket";
    static bool IsMultiParent(string type) => type is "Null" or "Control";

    void LoadStatic(Stream s, RigHierarchyElement e, int settingsVersion, bool local, bool global, bool initial, bool current)
    {
        void Add(string name, object value) => e.Fields.Add(new(name, value));
        if (!(IsTransform(e.Type) || e.Type is "Curve" or "Connector")) throw new FormatException($"Unknown rig element type {e.Type}");

        // FRigBaseElement::Load
        Add("Key", s.Key());
        if (s.CR >= HierarchyElementMetadata && s.CR < RigHierarchyStoresElementMetadata)
            Add("LegacyMetadata", s.Array(() => new KeyValuePair<string, object>(s.Name() + ":" + s.Name(), ReadMetadata(s)), 21));

        // FRigTransformElement::Load: the pose, current and initial, each local and global
        if (IsTransform(e.Type)) Add("Pose", ReadCurrentAndInitial(s, settingsVersion, local, global, initial, current));

        // FRigMultiParentElement::Load
        if (IsMultiParent(e.Type))
        {
            if (s.CR < RemovedMultiParentParentCache) Add("LegacyParentCache", ReadCurrentAndInitial(s, settingsVersion, local, global, initial, current));
            Add("NumParents", s.Int());
        }

        switch (e.Type)
        {
            case "Bone": Add("BoneType", s.Name()); break;
            case "Control":
                ReadControlSettings(s, Add);
                Add("Offset", ReadCurrentAndInitial(s, settingsVersion, local, global, initial, current));
                Add("Shape", ReadCurrentAndInitial(s, settingsVersion, local, global, initial, current));
                if (s.CR >= PreferredEulerAnglesForControls) Add("PreferredEulerAngles", new object[] { s.Name(), s.Reals(3), s.Reals(3) });
                break;
            case "Curve":
                if (s.CR >= CurveElementValueStateFlag) Add("IsValueSet", s.Bool());
                Add("Value", s.Float());
                break;
            case "Connector":
                Add("Description", s.Str());
                if (s.CR >= ConnectorsWithType) { Add("ConnectorType", s.Byte()); Add("Optional", s.Bool()); }
                if (s.CR >= RigHierarchyArrayConnectors) Add("IsArray", s.Bool());
                if (s.CR >= RigHierarchyPostConstructionConnectors) Add("PostConstruction", s.Bool());
                Add("Rules", s.Array(() => new[] { s.Str(), s.Str() }, 8));
                break;
        }
    }

    void LoadInter(Stream s, RigHierarchyElement e)
    {
        void Add(string name, object value) => e.Fields.Add(new(name, value));
        if (IsSingleParent(e.Type)) Add("Parent", s.Key());
        if (IsMultiParent(e.Type))
        {
            int parents = 0;
            foreach (var f in e.Fields) if (f.Key == "NumParents") parents = (int)f.Value;
            var list = new List<object>(parents);
            for (int p = 0; p < parents; p++)
            {
                var parent = new Dictionary<string, object> { ["Key"] = s.Key() };
                if (s.CR >= RigHierarchyMultiParentConstraints) { parent["InitialWeight"] = s.Floats(3); parent["Weight"] = s.Floats(3); }
                else { parent["InitialWeight"] = s.Float(); parent["Weight"] = s.Float(); }
                if (s.CR >= RigHierarchyParentContraintWithLabel) parent["DisplayLabel"] = s.Name();
                list.Add(parent);
            }
            Add("Parents", list);
        }
    }

    /// <summary>FRigCurrentAndInitialTransform, FRigLocalAndGlobalTransform and FRigComputedTransform loads.</summary>
    static Dictionary<string, object> ReadCurrentAndInitial(Stream s, int settingsVersion, bool local, bool global, bool initial, bool current)
    {
        var d = new Dictionary<string, object>();
        Dictionary<string, object> LocalGlobal()
        {
            var lg = new Dictionary<string, object>();
            if (local) lg["Local"] = ReadComputed(s, settingsVersion);
            if (global) lg["Global"] = ReadComputed(s, settingsVersion);
            return lg;
        }
        if (current) d["Current"] = LocalGlobal();
        if (initial) d["Initial"] = LocalGlobal();
        return d;
    }

    static object ReadComputed(Stream s, int settingsVersion)
    {
        if (settingsVersion < RigHierarchyCompactTransformSerialization) return new object[] { s.Transform(), s.Bool() };
        bool dirty = s.Bool();
        return dirty ? new object[] { true } : new object[] { false, ReadCompact(s) };
    }

    /// <summary>FRigCompactTransform::Load: a representation byte, then only the parts it says are present, as single floats.</summary>
    static object ReadCompact(Stream s)
    {
        byte state = s.Byte();
        float[] position = null, rotation = null, scale = null;
        float? uniform = null;
        switch (state)
        {
            case 0: break;
            case 1: uniform = s.Float(); break;
            case 2: scale = s.Floats(3); break;
            case 3: rotation = s.Floats(4); break;
            case 4: rotation = s.Floats(4); uniform = s.Float(); break;
            case 5: rotation = s.Floats(4); scale = s.Floats(3); break;
            case 6: position = s.Floats(3); break;
            case 7: position = s.Floats(3); uniform = s.Float(); break;
            case 8: position = s.Floats(3); scale = s.Floats(3); break;
            case 9: position = s.Floats(3); rotation = s.Floats(4); break;
            case 10: position = s.Floats(3); rotation = s.Floats(4); uniform = s.Float(); break;
            case 11: position = s.Floats(3); rotation = s.Floats(4); scale = s.Floats(3); break;
            case 12: return new object[] { state, s.Transform() };
            default: throw new FormatException($"Invalid compact transform representation {state}");
        }
        return new object[] { state, position, rotation, scale, uniform };
    }

    /// <summary>Rigs/RigHierarchyElements.cpp FRigControlSettings::Load.</summary>
    static void ReadControlSettings(Stream s, Action<string, object> add)
    {
        if (s.CR >= ControlAnimationType) add("AnimationType", s.Name());
        add("ControlType", s.Name());
        add("DisplayName", s.Name());
        add("PrimaryAxis", s.Name());
        add("IsCurve", s.Bool());
        if (s.CR < ControlAnimationType) add("Animatable", s.Bool());
        if (s.CR < PerChannelLimits) add("LegacyLimits", new[] { s.Bool(), s.Bool(), s.Bool() });
        else add("LimitEnabled", s.Array(() => new[] { s.Bool(), s.Bool() }, 8));
        add("DrawLimits", s.Bool());
        if (s.CR >= StorageMinMaxValuesAsFloatStorage) { add("MinimumValue", s.Floats(32)); add("MaximumValue", s.Floats(32)); }
        else { add("MinimumTransform", s.Transform()); add("MaximumTransform", s.Transform()); }
        if (s.CR < ControlAnimationType) add("ShapeEnabled", s.Bool());
        add("ShapeVisible", s.Bool());
        if (s.CR >= ControlAnimationType) add("ShapeVisibility", s.Name());
        add("ShapeName", s.Name());
        add("ShapeColor", s.Floats(4));
        add("IsTransientControl", s.Bool());
        add("ControlEnum", s.Str());
        if (s.CR >= RigHierarchyParentContraintWithLabel) add("AvailableSpaces", s.Array(() => new object[] { s.Key(), s.Name() }, 24));
        else if (s.CR >= RigHierarchyControlSpaceFavorites) add("AvailableSpaces", s.Array(() => s.Key(), 16));
        if (s.CR >= ControlAnimationType) add("DrivenControls", s.Array(() => s.Key(), 16));
        if (s.CR >= ControlAnimationType) add("GroupWithParentControl", s.Bool());
        if (s.CR >= RestrictSpaceSwitchingForControls) add("RestrictSpaceSwitching", s.Bool());
        if (s.CR >= ControlTransformChannelFiltering) add("FilteredChannels", s.Array(s.Byte, 1));
        if (s.CR >= RigHierarchyControlPreferredRotationOrder) add("PreferredRotationOrder", s.Byte());
        if (s.CR >= RigHierarchyControlPreferredRotationOrderFlag) add("UsePreferredRotationOrder", s.Bool());
    }

    /// <summary>URigHierarchy::FMetadataStorage::Serialize: entries of name, type name and metadata.</summary>
    static List<KeyValuePair<string, object>> ReadMetadataStorage(Stream s) =>
        s.Array(() => { string item = s.Name(); string type = s.Name(); return new KeyValuePair<string, object>(item + ":" + type, ReadMetadata(s)); }, 17);

    /// <summary>Rigs/RigHierarchyMetadata.h: FRigBaseMetadata::Serialize (name, type byte), then the typed value.</summary>
    static object ReadMetadata(Stream s)
    {
        string name = s.Name();
        byte type = s.Byte();
        if (type >= MetadataTypes.Length) throw new FormatException($"Invalid metadata type {type}");
        object One(int t) => (t / 2) switch
        {
            0 => s.Bool(),
            1 => s.Float(),
            2 => s.Int(),
            3 => s.Name(),
            4 => s.Reals(3),
            5 => s.Reals(3),
            6 => s.Reals(4),
            7 => s.Transform(),
            8 => s.Floats(4),
            _ => s.Key()
        };
        object value = type % 2 == 0 ? One(type) : s.Array(() => One(type), 1);
        return new object[] { name, MetadataTypes[type], value };
    }
}
