using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UAssetAPI.CustomVersions;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.ExportTypes;

/// <summary>One byte code instruction: its op code name and the values its op serializer reads, in order.</summary>
public class RigVMInstruction
{
    [JsonProperty] public string OpCode;
    [JsonProperty] public List<object> Values = new();
}

/// <summary>
/// RigVM RigVMCore/RigVM.cpp URigVM::Load: property paths, function names, byte code, parameters, literal and default work
/// memory, and the localized registry. A VM is a URigVM export of its own, or is serialized inline by a RigVM or Control
/// Rig blueprint generated class.
/// </summary>
public class RigVMData
{
    static readonly Guid RigVMGuid = UAPUtils.GUID(0xDC49959B, 0x53C04DE7, 0x9156EA88, 0x5E7C5D39);
    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid AnimGuid = UAPUtils.GUID(0xAF43A65D, 0x7FD34947, 0x98733E8E, 0xD9C1BB05);

    // FRigVMObjectVersion
    const int AddedVMHashChecks = 1, PredicatesAddedToExecuteOps = 2, VMStoringUserDefinedStructMap = 3, VMStoringUserDefinedEnumMap = 4,
        HostStoringUserDefinedData = 5, VMMemoryStorageStructSerialized = 6, VMMemoryStorageDefaultsGeneratedAtVM = 7,
        VMBytecodeStorePublicContextPath = 8, VMBytecodeStorePublicContextPathAsTopLevelAssetPath = 17, ByteCodeCleanup = 18,
        VMRemoveTooltipFromFunctionHeader = 9, RemoveLibraryNodeReferenceFromFunctionIdentifier = 10, AddVariantToFunctionIdentifier = 11,
        FunctionHeaderStoresLayout = 13, FunctionHeaderLayoutStoresPinIndexInCategory = 14, FunctionHeaderLayoutStoresCategoryExpansion = 15,
        RigVMSaveSerializedGraphInGraphFunctionDataAsByteArray = 16, LocalizedRegistry = 19, LocalizedRegistryWithRelativeSeekOffset = 20,
        FunctionArgumentCanRepresentInputVariable = 21, ObjectArchiveVersionContainerSerialization = 22, DebugOperandMappingSimplified = 23,
        RigVMCallables = 24, GuidForVariables = 25;
    // FUE5MainStreamObjectVersion, FFortniteMainBranchObjectVersion, FUE5ReleaseStreamObjectVersion, FAnimObjectVersion
    const int RigVMCopyOpStoreNumBytes = 43, RigVMMemoryStorageObject = 52, RigVMLazyEvaluation = 94, RigVMSerializeExecuteContextStruct = 97,
        RigVMExternalExecuteContextStruct = 114, RigVMSaveSerializedGraphInGraphFunctionData = 101, RigVMByteCodeDeterminism = 36, FortniteRigVMSaveDebugMap = 113, ReleaseRigVMSaveDebugMap = 44,
        StoreMarkerNamesOnSkeleton = 2, SerializeRigVMRegisterArrayState = 3, SerializeRigVMRegisterDynamicState = 8, SerializeRigVMEntries = 10;

    static readonly string[] OpCodes = BuildOpCodes();
    static string[] BuildOpCodes()
    {
        var names = new List<string>();
        for (int i = 0; i <= 64; i++) names.Add($"Execute_{i}_Operands");
        names.AddRange(new[] { "Zero", "BoolFalse", "BoolTrue", "Copy", "Increment", "Decrement", "Equals", "NotEquals", "JumpAbsolute",
            "JumpForward", "JumpBackward", "JumpAbsoluteIf", "JumpForwardIf", "JumpBackwardIf", "ChangeType", "Exit", "BeginBlock", "EndBlock",
            "ArrayReset", "ArrayGetNum", "ArraySetNum", "ArrayGetAtIndex", "ArraySetAtIndex", "ArrayAdd", "ArrayInsert", "ArrayRemove",
            "ArrayFind", "ArrayAppend", "ArrayClone", "ArrayIterator", "ArrayUnion", "ArrayDifference", "ArrayIntersection", "ArrayReverse",
            "InvokeEntry", "JumpToBranch", "Execute", "RunInstructions", "SetupTraits", "InvokeCallable" });
        return names.ToArray();
    }

    [JsonProperty] public Dictionary<string, object> Fields = new();
    [JsonProperty] public List<RigVMInstruction> Instructions;
    /// <summary>A VM saved before FRigVMObjectVersion with class-based storage: URigVM::Load reads it and then discards the VM, which
    /// the owning rig recompiles, so the bytes after the storage flag never reach the running VM.</summary>
    [JsonProperty] public bool LegacyDiscardedOnLoad;
    /// <summary>The localized registry, which URigVM::Load seeks past unless a.RigVM.EnableLocalizedRegistry is set.</summary>
    [JsonProperty] public Dictionary<string, object> LocalizedRegistryData;
    /// <summary>Parses a serialized VM, given its bytes and their offset in the package file; throws unless it reads every byte.</summary>
    public static RigVMData Parse(UAsset asset, byte[] data, long fileOffset) => Parse(asset, data, fileOffset, true, out _);

    /// <summary>Parses a VM at the start of the bytes; with requireEnd false, other data may follow it, and consumed says where it ends.</summary>
    public static RigVMData Parse(UAsset asset, byte[] data, long fileOffset, bool requireEnd, out int consumed)
    {
        var vm = new RigVMData { requireEnd = requireEnd };
        vm.ParseInto(asset, data, fileOffset);
        consumed = vm.consumed;
        return vm;
    }

    bool requireEnd = true;
    int consumed;

    void ParseInto(UAsset asset, byte[] Data, long SerialOffset)
    {
        var r = new AssetBinaryReader(new MemoryStream(Data), asset);
        var (vm, ue5, anim, fortnite, release) = Versions(asset);
        if (anim < StoreMarkerNamesOnSkeleton) { End(r); return; }

        var c = new Cursor(r);
        void UserDefinedMap(string name) => Fields[name] = c.Array(() => new object[] { c.Str(), new FSoftObjectPath(r) }, 8);

        if (vm < 0)
        {
            int storage = 1;
            if (ue5 >= RigVMMemoryStorageObject) storage = r.ReadInt32();
            Fields["ClassBasedStorage"] = storage;
            if (ue5 < RigVMExternalExecuteContextStruct && ue5 >= RigVMSerializeExecuteContextStruct) Fields["ExecuteContextPath"] = c.Str();
            if (storage != 0)
            {
                // URigVM::Load reads the class-based storage, then resets the VM
                LegacyDiscardedOnLoad = true;
                Fields["WorkMemory"] = ReadLegacyMemoryContainer(r, c, anim);
                Fields["LiteralMemory"] = ReadLegacyMemoryContainer(r, c, anim);
                Fields["FunctionNames"] = c.Array(c.Name, 8);
                ReadByteCode(r, c, vm, ue5, fortnite, anim);
                Fields["Parameters"] = c.Array(() => new object[] { r.ReadByte(), c.Name(), r.ReadInt32(), c.Str(), c.Name() }, 21);
                if (ue5 >= RigVMCopyOpStoreNumBytes)
                {
                    if (vm >= VMStoringUserDefinedStructMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedStructGuidToPathName");
                    if (vm >= VMStoringUserDefinedEnumMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedEnumToPathName");
                }
                End(r);
                return;
            }
        }
        if (vm >= AddedVMHashChecks) Fields["CachedVMHash"] = r.ReadUInt32();
        Fields["ExternalPropertyPathDescriptions"] = c.Array(c.PathDescription, 12);
        Fields["FunctionNames"] = c.Array(c.Name, 8);
        ReadByteCode(r, c, vm, ue5, fortnite, anim);
        Fields["Parameters"] = c.Array(() => new object[] { r.ReadByte(), c.Name(), r.ReadInt32(), c.Str(), c.Name() }, 21);
        if ((release >= ReleaseRigVMSaveDebugMap || fortnite >= FortniteRigVMSaveDebugMap) && vm < DebugOperandMappingSimplified)
            Fields["PreviousOperandToDebugRegisters"] = c.Array(() => new object[] { c.Operand(), c.Array(c.Operand, 5) }, 9);
        if (vm >= VMStoringUserDefinedStructMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedStructGuidToPathName");
        if (vm >= VMStoringUserDefinedEnumMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedEnumToPathName");
        if (vm >= VMMemoryStorageStructSerialized) Fields["LiteralMemory"] = ReadMemoryStorage(r, c);
        if (vm >= VMMemoryStorageDefaultsGeneratedAtVM)
        {
            Fields["DefaultWorkMemory"] = ReadMemoryStorage(r, c);
            Fields["DefaultDebugMemory"] = ReadMemoryStorage(r, c);
        }
        if (vm >= LocalizedRegistry)
        {
            bool stored = c.Bool();
            Fields["StoredLocalizedRegistry"] = stored;
            if (stored)
            {
                long before = r.BaseStream.Position;
                long offset = r.ReadInt64();
                // the relative form counts from before the offset; the older absolute form is a package file offset
                long target = vm >= LocalizedRegistryWithRelativeSeekOffset ? before + offset : offset - SerialOffset;
                if (target < r.BaseStream.Position || target > r.BaseStream.Length) throw new FormatException($"Invalid localized registry end {target}");
                LocalizedRegistryData = ReadLocalizedRegistry(r, c);
                if (r.BaseStream.Position != target) throw new FormatException($"Localized registry ends at {r.BaseStream.Position}, not {target}");
            }
        }
        End(r);
    }

    /// <summary>RigVMRegistry.cpp FRigVMRegistry_NoLock::Serialize_NoLock: the allowed classes; the types, each a C++ type name and
    /// type object; the dispatch factories, each its struct and flattened arguments (name, direction and the types of every
    /// permutation used); and the functions (RigVMFunction.cpp), each its name, struct, factory struct and arguments (name, type
    /// and direction).</summary>
    static Dictionary<string, object> ReadLocalizedRegistry(AssetBinaryReader r, Cursor c)
    {
        object Object() => new FPackageIndex(r);
        object Type() => new object[] { c.Name(), Object() };
        return new Dictionary<string, object>
        {
            ["AllowedClasses"] = c.Array(Object, 4),
            ["Types"] = c.Array(Type, 12),
            ["Factories"] = c.Array(() => new Dictionary<string, object> { ["Struct"] = Object(),
                ["Arguments"] = c.Array(() => new object[] { c.Name(), r.ReadInt32(), c.Array(Type, 12) }, 16) }, 8),
            ["Functions"] = c.Array(() => new Dictionary<string, object> { ["Name"] = c.Str(), ["Struct"] = Object(), ["Factory"] = Object(),
                ["Arguments"] = c.Array(() => new object[] { c.Str(), c.Str(), r.ReadInt32() }, 12) }, 16),
        };
    }

    static (int vm, int ue5, int anim, int fortnite, int release) Versions(UAsset asset) =>
        (asset.GetCustomVersion(RigVMGuid), asset.GetCustomVersion(UE5MainGuid), asset.GetCustomVersion(AnimGuid),
         (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>(), (int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>());

    /// <summary>Reads the values the RigVM serializers share, checking counts against the bytes left.</summary>
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
        /// <summary>A TMap: its pairs in serialized order.</summary>
        public List<object[]> Map(Func<object> key, Func<object> value, int minBytes) => Array(() => new[] { key(), value() }, minBytes);
        public string Str() => r.ReadFString()?.Value;
        public string Name() => r.ReadFName().ToString();
        // FRigVMOperand: memory type, register index, register offset
        public object Operand() => new object[] { r.ReadByte(), r.ReadUInt16(), r.ReadUInt16() };
        public object PathDescription() => new object[] { r.ReadInt32(), Str(), Str() };
        public bool Bool() { uint v = r.ReadUInt32(); if (v > 1) throw new FormatException($"Invalid bool {v}"); return v == 1; }
        public object SoftPath() => new FSoftObjectPath(r);
        public object Text() { var t = new TextPropertyData(FName.DefineDummy(r.Asset, "Text")); t.Read(r, false, 0); return t; }
        public object Color() => new[] { r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() };
        public object Guid() => r.ReadGuid();
    }

    /// <summary>
    /// RigVMCore/RigVMGraphFunctionDefinition.cpp: a FRigVMGraphFunctionStore's public functions, which a RigVM or Control Rig
    /// generated class serializes after its VM (its private functions are written only for reference collection). Each function
    /// is its header, its compilation data and its collapse node archive; the archive's buffer is the function graph's own
    /// serialized node, kept as bytes.
    /// </summary>
    public static List<Dictionary<string, object>> ReadGraphFunctions(AssetBinaryReader r)
    {
        var (vm, ue5, anim, fortnite, release) = Versions(r.Asset);
        var c = new Cursor(r);
        return c.Array(() => ReadGraphFunction(r, c, vm, ue5, anim, fortnite, release), 64);
    }

    static Dictionary<string, object> ReadGraphFunction(AssetBinaryReader r, Cursor c, int vm, int ue5, int anim, int fortnite, int release)
    {
        var f = new Dictionary<string, object>();
        // FRigVMGraphFunctionIdentifier: library node path, host object
        object Identifier() => new object[] { vm < RemoveLibraryNodeReferenceFromFunctionIdentifier ? c.SoftPath() : c.Str(), c.SoftPath() };
        f["LibraryPointer"] = Identifier();
        // FRigVMVariant: guid, then FRigVMTag name, label, tooltip, color, shown in the interface, marks the subject invalid
        if (vm >= AddVariantToFunctionIdentifier)
            f["Variant"] = new object[] { c.Guid(), c.Array(() => new object[] { c.Name(), c.Str(), c.Text(), c.Color(), c.Bool(), c.Bool() }, 37) };
        f["Name"] = c.Name();
        f["NodeTitle"] = c.Str();
        f["NodeColor"] = c.Color();
        if (vm < VMRemoveTooltipFromFunctionHeader) f["Tooltip"] = c.Text();
        else f["Description"] = c.Str();
        f["Category"] = c.Str();
        f["Keywords"] = c.Str();
        // FRigVMGraphFunctionArgument::Serialize
        f["Arguments"] = c.Array(() => new object[] { c.Name(), c.Name(), c.Name(), c.SoftPath(), c.Bool(), r.ReadByte(), c.Str(), c.Bool(),
            c.Map(c.Str, c.Text, 9), vm >= FunctionArgumentCanRepresentInputVariable ? c.Bool() : null }, 45);
        f["Dependencies"] = c.Map(Identifier, () => r.ReadUInt32(), 12);
        // FRigVMExternalVariableDef: guid, name, base C++ type, type object, array, public, read only, and a size that is always 0
        f["ExternalVariables"] = c.Array(() => new object[] { vm >= GuidForVariables ? c.Guid() : null, c.Name(), c.Name(), c.SoftPath(),
            c.Bool(), c.Bool(), c.Bool(), r.ReadInt32() }, 36);
        // FRigVMNodeLayout: pin categories (path, elements, expanded by default), pin index in category, display names
        if (vm >= FunctionHeaderStoresLayout)
            f["Layout"] = new object[] {
                c.Array(() => new object[] { c.Str(), c.Array(c.Str, 4), vm >= FunctionHeaderLayoutStoresCategoryExpansion ? c.Bool() : null }, 8),
                vm >= FunctionHeaderLayoutStoresPinIndexInCategory ? c.Map(c.Str, () => r.ReadInt32(), 8) : null,
                c.Map(c.Str, c.Str, 8) };

        // FRigVMFunctionCompilationData
        var code = new RigVMData();
        code.ReadByteCode(r, c, vm, ue5, fortnite, anim);
        var data = new Dictionary<string, object> { ["ByteCode"] = code, ["FunctionNames"] = c.Array(c.Name, 8) };
        object Description() => new object[] { c.Name(), c.Str(), c.SoftPath(), c.Str() };
        foreach (var kind in new object[] { "Work", "Literal", "Debug", "External" })
        {
            data[kind + "PropertyDescriptions"] = c.Array(Description, 20);
            data[kind + "PropertyPathDescriptions"] = c.Array(c.PathDescription, 12);
        }
        data["ExternalRegisterIndexToVariable"] = c.Map(() => r.ReadInt32(), c.Name, 12);
        data["Operands"] = c.Map(c.Str, c.Operand, 9);
        if (vm >= RigVMCallables) data["InterfaceOperands"] = c.Map(c.Name, c.Operand, 13);
        data["Hash"] = r.ReadUInt32();
        // the operand to debug register map, read and discarded, with byte counts
        if ((release >= ReleaseRigVMSaveDebugMap || fortnite >= FortniteRigVMSaveDebugMap) && vm < DebugOperandMappingSimplified)
        {
            var keys = new List<object>();
            for (int k = r.ReadByte(); k > 0; k--)
            {
                var key = c.Operand();
                var values = new List<object>();
                for (int v = r.ReadByte(); v > 0; v--) values.Add(c.Operand());
                keys.Add(new object[] { key, values });
            }
            data["PreviousOperandToDebugRegisters"] = keys;
        }
        f["CompilationData"] = data;

        if (ue5 < RigVMSaveSerializedGraphInGraphFunctionData) return f;
        f["SerializedCollapsedNode"] = c.Str();
        if (vm < RigVMSaveSerializedGraphInGraphFunctionDataAsByteArray) return f;
        // FRigVMObjectArchive: versions, buffer, uncompressed size, compressed size, compressed
        var versions = vm >= ObjectArchiveVersionContainerSerialization ? c.Map(c.Guid, () => r.ReadInt32(), 20) : null;
        byte[] buffer = r.ReadBytes(c.Count(1));
        int uncompressed = r.ReadInt32(), compressed = r.ReadInt32();
        f["CollapseNodeArchive"] = RigVMObjectArchiveReader.Decode((UAsset)r.Asset, versions, buffer, uncompressed, compressed, c.Bool());
        return f;
    }

    void End(AssetBinaryReader r)
    {
        consumed = (int)r.BaseStream.Position;
        if (requireEnd && r.BaseStream.Position != r.BaseStream.Length) throw new FormatException($"{r.BaseStream.Length - r.BaseStream.Position} bytes after the VM");
    }

    /// <summary>RigVMCore/RigVMMemoryDeprecated.cpp FRigVMMemoryContainer::Load: the name-map flag, memory type, registers,
    /// register offsets, script struct paths and total bytes, then each register's values (Plain as bytes, Name as names,
    /// String as strings, Struct as one ImportText string per element; dynamic registers save empty arrays, one per slice
    /// when nested).</summary>
    static object ReadLegacyMemoryContainer(AssetBinaryReader r, Cursor c, int anim)
    {
        if (anim < StoreMarkerNamesOnSkeleton) return null;
        int release = (int)r.Asset.GetCustomVersion<FReleaseObjectVersion>();
        var d = new Dictionary<string, object> { ["UseNameMap"] = c.Bool(), ["MemoryType"] = r.ReadByte() };
        // FRigVMRegister::Serialize and Load
        var registers = c.Array(() => new Dictionary<string, object>
        {
            ["Type"] = r.ReadByte(), ["ByteIndex"] = r.ReadUInt32(), ["ElementSize"] = r.ReadUInt16(), ["ElementCount"] = r.ReadUInt16(),
            ["SliceIndex"] = r.ReadUInt16(), ["SliceCount"] = r.ReadUInt16(), ["AlignmentBytes"] = r.ReadByte(), ["TrailingBytes"] = r.ReadUInt16(),
            ["Name"] = c.Name(), ["ScriptStructIndex"] = r.ReadInt32(),
            ["IsArray"] = anim >= SerializeRigVMRegisterArrayState && c.Bool(),
            ["IsDynamic"] = anim >= SerializeRigVMRegisterDynamicState && c.Bool(),
        }, 26);
        d["Registers"] = registers;
        // FRigVMRegisterOffset::Serialize and Load
        d["RegisterOffsets"] = c.Array(() =>
        {
            var o = new Dictionary<string, object> { ["Segments"] = c.Array(() => r.ReadInt32(), 4), ["Type"] = r.ReadByte(), ["CPPType"] = c.Name() };
            o["ScriptStruct"] = release < (int)FReleaseObjectVersion.SerializeRigVMOffsetSegmentPaths ? c.Name() : new FPackageIndex(r);
            o["ElementSize"] = r.ReadUInt16();
            if (release >= (int)FReleaseObjectVersion.SerializeRigVMOffsetSegmentPaths)
            {
                o["ParentScriptStruct"] = new FPackageIndex(r);
                o["SegmentPath"] = c.Str();
                o["ArrayIndex"] = r.ReadInt32();
            }
            return o;
        }, 15);
        d["ScriptStructPaths"] = c.Array(c.Str, 4);
        d["TotalBytes"] = r.ReadUInt64();
        // a Plain register's elements are copied as bytes; only the bytecode that uses the register gives them a type
        object View(byte type, ushort size) => type switch
        {
            0 => new Payload($"plain register elements of {size} bytes", r.ReadBytes(c.Count(1))),
            1 => c.Array(c.Str, 4),
            2 => c.Array(c.Name, 8),
            3 => c.Array(c.Str, 4),
            _ => throw new FormatException($"Invalid register type {type}"),
        };
        var values = new List<object>(registers.Count);
        foreach (var reg in registers)
        {
            byte type = (byte)reg["Type"];
            bool dynamic = (bool)reg["IsDynamic"], nested = dynamic && (bool)reg["IsArray"];
            if ((ushort)reg["ElementCount"] == 0 && !dynamic) { values.Add(null); continue; }
            ushort size = (ushort)reg["ElementSize"];
            if (!nested) values.Add(View(type, size));
            else { var slices = new List<object>(); for (int i = 0; i < (ushort)reg["SliceCount"]; i++) slices.Add(View(type, size)); values.Add(slices); }
        }
        d["Values"] = values;
        return d;
    }

    /// <summary>RigVMCore/RigVMMemoryStorageStruct.cpp: the FInstancedPropertyBag, then the memory type and property paths.</summary>
    static object ReadMemoryStorage(AssetBinaryReader r, Cursor c)
    {
        var bag = new InstancedPropertyBagPropertyData(FName.DefineDummy(r.Asset, "MemoryStorage"));
        bag.Read(r, false, 0);
        return new object[] { bag, r.ReadByte(), c.Array(c.PathDescription, 12) };
    }

    /// <summary>RigVMCore/RigVMByteCode.cpp FRigVMByteCode::Load: each instruction's op code, then its op as that op serializes itself.</summary>
    void ReadByteCode(AssetBinaryReader r, Cursor c, int vm, int ue5, int fortnite, int anim)
    {
        if (fortnite < RigVMByteCodeDeterminism) { Fields["ByteCode"] = r.ReadBytes(c.Count(1)); return; }
        int instructions = c.Count(1);
        Instructions = new List<RigVMInstruction>(instructions);
        for (int i = 0; i < instructions; i++)
        {
            byte code = r.ReadByte();
            if (code >= OpCodes.Length) throw new FormatException($"Invalid op code {code} at {r.BaseStream.Position - 1}");
            var ins = new RigVMInstruction { OpCode = OpCodes[code] };
            Instructions.Add(ins);
            void V(object v) => ins.Values.Add(v);
            void Operands(int n) { for (int k = 0; k < n; k++) V(c.Operand()); }
            string op = code <= 64 ? "Execute" : OpCodes[code];
            switch (op)
            {
                case "Execute":
                case "InvokeCallable":
                {
                    // FRigVMInvokeCallableOp::Serialize repeats the op code; an Execute_N code carries its operand count
                    byte inner = r.ReadByte();
                    V(inner);
                    V(r.ReadUInt16());
                    int arguments = inner <= 64 ? inner : r.ReadUInt16();
                    if (inner > 64) V(arguments);
                    if (op == "Execute" && vm >= PredicatesAddedToExecuteOps) { V(r.ReadUInt16()); V(r.ReadUInt16()); }
                    Operands(arguments);
                    break;
                }
                case "Copy":
                    V(r.ReadByte()); Operands(2);
                    if (ue5 >= RigVMCopyOpStoreNumBytes)
                    {
                        if (vm < ByteCodeCleanup) V(r.ReadUInt16());
                        V(r.ReadByte());
                    }
                    break;
                case "Zero": case "BoolFalse": case "BoolTrue": case "Increment": case "Decrement": case "ArrayReset": case "ArrayReverse": case "SetupTraits":
                    V(r.ReadByte()); Operands(1); break;
                case "Equals": case "NotEquals": V(r.ReadByte()); Operands(3); break;
                case "JumpAbsolute": case "JumpForward": case "JumpBackward": V(r.ReadByte()); V(r.ReadInt32()); break;
                case "JumpAbsoluteIf": case "JumpForwardIf": case "JumpBackwardIf": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); V(c.Bool()); break;
                case "Exit": case "EndBlock": case "ChangeType": break;
                case "BeginBlock": case "ArrayGetNum": case "ArraySetNum": case "ArrayAppend": case "ArrayClone": case "ArrayRemove": case "ArrayUnion":
                    V(r.ReadByte()); Operands(2); break;
                case "ArrayAdd": case "ArrayGetAtIndex": case "ArraySetAtIndex": case "ArrayInsert": case "ArrayDifference": case "ArrayIntersection":
                    V(r.ReadByte()); Operands(3); break;
                case "ArrayFind": V(r.ReadByte()); Operands(4); break;
                case "ArrayIterator": V(r.ReadByte()); Operands(6); break;
                case "InvokeEntry": V(c.Str()); break;
                case "JumpToBranch": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); break;
                case "RunInstructions": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); V(r.ReadInt32()); break;
                default: throw new FormatException($"Unhandled op code {op}");
            }
        }
        if (anim >= SerializeRigVMEntries) Fields["Entries"] = c.Array(() => c.Str(), 4);
        if (ue5 >= RigVMLazyEvaluation)
            Fields["BranchInfos"] = c.Array(() => new object[] { r.ReadInt32(), c.Str(), r.ReadInt32(), r.ReadInt32(),
                vm < ByteCodeCleanup ? r.ReadUInt16() : r.ReadInt32(), vm < ByteCodeCleanup ? r.ReadUInt16() : r.ReadInt32() }, 16);
        if (vm >= RigVMCallables)
            Fields["CallableInfos"] = c.Array(() => new object[] { r.ReadInt32(), c.Name(), r.ReadUInt32(),
                c.Array(() => new object[] { c.Name(), c.Str(), c.Operand(), c.Operand(), r.ReadByte() }, 23), r.ReadInt32(), r.ReadInt32() }, 28);
        if (vm >= VMBytecodeStorePublicContextPathAsTopLevelAssetPath) Fields["PublicContextAssetPath"] = new[] { c.Name(), c.Name() };
        else if (vm >= VMBytecodeStorePublicContextPath) Fields["PublicContextPath"] = c.Str();
    }
}

/// <summary>A URigVM export. URigVM::Serialize calls no base serializer, so the export is the VM; it keeps its bytes for writing.</summary>
public class RigVMExport : CustomSerializedExport
{
    [JsonProperty] public RigVMData VM;
    [JsonProperty] public string ParseFailure;

    public bool FullyParsed => ParseFailure == null;

    public RigVMExport() { }

    public override void Read(AssetBinaryReader reader, int nextStarting = 0)
    {
        base.Read(reader, nextStarting);
        try { VM = RigVMData.Parse(reader.Asset, Data, SerialOffset); }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException)
        {
            ParseFailure = $"{ex.GetType().Name}: {ex.Message}";
        }
    }
}
