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
/// RigVM RigVMCore/RigVM.cpp URigVM::Load, which reads the whole export: property paths, function names, byte code,
/// parameters, literal and default work memory, and the localized registry. The export keeps its bytes for writing; the
/// parse is a view of them.
/// </summary>
public class RigVMExport : CustomSerializedExport
{
    static readonly Guid RigVMGuid = UAPUtils.GUID(0xDC49959B, 0x53C04DE7, 0x9156EA88, 0x5E7C5D39);
    static readonly Guid UE5MainGuid = UAPUtils.GUID(0x697DD581, 0xE64F41AB, 0xAA4A51EC, 0xBEB7B628);
    static readonly Guid AnimGuid = UAPUtils.GUID(0xAF43A65D, 0x7FD34947, 0x98733E8E, 0xD9C1BB05);

    // FRigVMObjectVersion
    const int AddedVMHashChecks = 1, PredicatesAddedToExecuteOps = 2, VMStoringUserDefinedStructMap = 3, VMStoringUserDefinedEnumMap = 4,
        HostStoringUserDefinedData = 5, VMMemoryStorageStructSerialized = 6, VMMemoryStorageDefaultsGeneratedAtVM = 7,
        VMBytecodeStorePublicContextPath = 8, VMBytecodeStorePublicContextPathAsTopLevelAssetPath = 17, ByteCodeCleanup = 18,
        LocalizedRegistry = 19, LocalizedRegistryWithRelativeSeekOffset = 20, DebugOperandMappingSimplified = 23, RigVMCallables = 24;
    // FUE5MainStreamObjectVersion, FFortniteMainBranchObjectVersion, FUE5ReleaseStreamObjectVersion, FAnimObjectVersion
    const int RigVMCopyOpStoreNumBytes = 43, RigVMMemoryStorageObject = 52, RigVMLazyEvaluation = 94, RigVMSerializeExecuteContextStruct = 97,
        RigVMExternalExecuteContextStruct = 114, RigVMByteCodeDeterminism = 36, FortniteRigVMSaveDebugMap = 113, ReleaseRigVMSaveDebugMap = 44,
        StoreMarkerNamesOnSkeleton = 2, SerializeRigVMEntries = 10;

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
    /// <summary>The localized registry's bytes: URigVM::Load seeks past them unless a.RigVM.EnableLocalizedRegistry is set.</summary>
    [JsonProperty] public byte[] LocalizedRegistryBytes;
    [JsonProperty] public string ParseFailure;

    public bool FullyParsed => ParseFailure == null;

    public RigVMExport() { }

    public override void Read(AssetBinaryReader reader, int nextStarting = 0)
    {
        base.Read(reader, nextStarting);
        try { Parse(reader.Asset); }
        catch (Exception ex) when (ex is FormatException or EndOfStreamException or ArgumentException or InvalidOperationException)
        {
            ParseFailure = $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    void Parse(UAsset asset)
    {
        var r = new AssetBinaryReader(new MemoryStream(Data), asset);
        int vm = asset.GetCustomVersion(RigVMGuid), ue5 = asset.GetCustomVersion(UE5MainGuid), anim = asset.GetCustomVersion(AnimGuid);
        int fortnite = (int)asset.GetCustomVersion<FFortniteMainBranchObjectVersion>(), release = (int)asset.GetCustomVersion<FUE5ReleaseStreamObjectVersion>();
        if (anim < StoreMarkerNamesOnSkeleton) { End(r); return; }

        int Count(int minBytes)
        {
            int n = r.ReadInt32();
            if (n < 0 || (long)n * minBytes > r.BaseStream.Length - r.BaseStream.Position) throw new FormatException($"Invalid count {n} at {r.BaseStream.Position - 4}");
            return n;
        }
        List<T> Array<T>(Func<T> read, int minBytes) { int n = Count(minBytes); var l = new List<T>(n); for (int i = 0; i < n; i++) l.Add(read()); return l; }
        string Str() => r.ReadFString()?.Value;
        string Name() => r.ReadFName().ToString();
        // FRigVMOperand: memory type, register index, register offset
        object Operand() => new object[] { r.ReadByte(), r.ReadUInt16(), r.ReadUInt16() };
        object PathDescription() => new object[] { r.ReadInt32(), Str(), Str() };
        bool Bool() { uint v = r.ReadUInt32(); if (v > 1) throw new FormatException($"Invalid bool {v}"); return v == 1; }
        void UserDefinedMap(string name) => Fields[name] = Array(() => new object[] { Str(), new FSoftObjectPath(r) }, 8);

        if (vm < 0)
        {
            int storage = 1;
            if (ue5 >= RigVMMemoryStorageObject) storage = r.ReadInt32();
            Fields["ClassBasedStorage"] = storage;
            if (ue5 < RigVMExternalExecuteContextStruct && ue5 >= RigVMSerializeExecuteContextStruct) Fields["ExecuteContextPath"] = Str();
            if (storage != 0) { LegacyDiscardedOnLoad = true; return; }
        }
        if (vm >= AddedVMHashChecks) Fields["CachedVMHash"] = r.ReadUInt32();
        Fields["ExternalPropertyPathDescriptions"] = Array(PathDescription, 12);
        Fields["FunctionNames"] = Array(Name, 8);
        ReadByteCode(r, vm, ue5, fortnite, anim, Count, Str, Name, Operand, Bool, Array);
        Fields["Parameters"] = Array(() => new object[] { r.ReadByte(), Name(), r.ReadInt32(), Str(), Name() }, 21);
        if ((release >= ReleaseRigVMSaveDebugMap || fortnite >= FortniteRigVMSaveDebugMap) && vm < DebugOperandMappingSimplified)
            Fields["PreviousOperandToDebugRegisters"] = Array(() => new object[] { Operand(), Array(Operand, 5) }, 9);
        if (vm >= VMStoringUserDefinedStructMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedStructGuidToPathName");
        if (vm >= VMStoringUserDefinedEnumMap && vm < HostStoringUserDefinedData) UserDefinedMap("UserDefinedEnumToPathName");
        if (vm >= VMMemoryStorageStructSerialized) Fields["LiteralMemory"] = ReadMemoryStorage(r, Array, PathDescription);
        if (vm >= VMMemoryStorageDefaultsGeneratedAtVM)
        {
            Fields["DefaultWorkMemory"] = ReadMemoryStorage(r, Array, PathDescription);
            Fields["DefaultDebugMemory"] = ReadMemoryStorage(r, Array, PathDescription);
        }
        if (vm >= LocalizedRegistry)
        {
            bool stored = Bool();
            Fields["StoredLocalizedRegistry"] = stored;
            if (stored)
            {
                long before = r.BaseStream.Position;
                long offset = r.ReadInt64();
                // the relative form counts from before the offset; the older absolute form is a package file offset
                long target = vm >= LocalizedRegistryWithRelativeSeekOffset ? before + offset : offset - SerialOffset;
                if (target < r.BaseStream.Position || target > r.BaseStream.Length) throw new FormatException($"Invalid localized registry end {target}");
                LocalizedRegistryBytes = r.ReadBytes(checked((int)(target - r.BaseStream.Position)));
            }
        }
        End(r);
    }

    static void End(AssetBinaryReader r)
    {
        if (r.BaseStream.Position != r.BaseStream.Length) throw new FormatException($"{r.BaseStream.Length - r.BaseStream.Position} bytes after the VM");
    }

    /// <summary>RigVMCore/RigVMMemoryStorageStruct.cpp: the FInstancedPropertyBag, then the memory type and property paths.</summary>
    static object ReadMemoryStorage(AssetBinaryReader r, Func<Func<object>, int, List<object>> array, Func<object> pathDescription)
    {
        var bag = new InstancedPropertyBagPropertyData(FName.DefineDummy(r.Asset, "MemoryStorage"));
        bag.Read(r, false, 0);
        return new object[] { bag, r.ReadByte(), array(pathDescription, 12) };
    }

    /// <summary>RigVMCore/RigVMByteCode.cpp FRigVMByteCode::Load: each instruction's op code, then its op as that op serializes itself.</summary>
    void ReadByteCode(AssetBinaryReader r, int vm, int ue5, int fortnite, int anim, Func<int, int> count, Func<string> str, Func<string> name,
        Func<object> operand, Func<bool> boolean, Func<Func<object>, int, List<object>> array)
    {
        if (fortnite < RigVMByteCodeDeterminism) { Fields["ByteCode"] = r.ReadBytes(count(1)); return; }
        int instructions = count(1);
        Instructions = new List<RigVMInstruction>(instructions);
        for (int i = 0; i < instructions; i++)
        {
            byte code = r.ReadByte();
            if (code >= OpCodes.Length) throw new FormatException($"Invalid op code {code} at {r.BaseStream.Position - 1}");
            var ins = new RigVMInstruction { OpCode = OpCodes[code] };
            Instructions.Add(ins);
            void V(object v) => ins.Values.Add(v);
            void Operands(int n) { for (int k = 0; k < n; k++) V(operand()); }
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
                case "JumpAbsoluteIf": case "JumpForwardIf": case "JumpBackwardIf": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); V(boolean()); break;
                case "Exit": case "EndBlock": case "ChangeType": break;
                case "BeginBlock": case "ArrayGetNum": case "ArraySetNum": case "ArrayAppend": case "ArrayClone": case "ArrayRemove": case "ArrayUnion":
                    V(r.ReadByte()); Operands(2); break;
                case "ArrayAdd": case "ArrayGetAtIndex": case "ArraySetAtIndex": case "ArrayInsert": case "ArrayDifference": case "ArrayIntersection":
                    V(r.ReadByte()); Operands(3); break;
                case "ArrayFind": V(r.ReadByte()); Operands(4); break;
                case "ArrayIterator": V(r.ReadByte()); Operands(6); break;
                case "InvokeEntry": V(str()); break;
                case "JumpToBranch": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); break;
                case "RunInstructions": V(r.ReadByte()); Operands(1); V(r.ReadInt32()); V(r.ReadInt32()); break;
                default: throw new FormatException($"Unhandled op code {op}");
            }
        }
        if (anim >= SerializeRigVMEntries) Fields["Entries"] = array(() => str(), 4);
        if (ue5 >= RigVMLazyEvaluation)
            Fields["BranchInfos"] = array(() => new object[] { r.ReadInt32(), str(), r.ReadInt32(), r.ReadInt32(),
                vm < ByteCodeCleanup ? r.ReadUInt16() : r.ReadInt32(), vm < ByteCodeCleanup ? r.ReadUInt16() : r.ReadInt32() }, 16);
        if (vm >= RigVMCallables)
            Fields["CallableInfos"] = array(() => new object[] { r.ReadInt32(), name(), r.ReadUInt32(),
                array(() => new object[] { name(), str(), operand(), operand(), r.ReadByte() }, 23), r.ReadInt32(), r.ReadInt32() }, 28);
        if (vm >= VMBytecodeStorePublicContextPathAsTopLevelAssetPath) Fields["PublicContextAssetPath"] = new[] { name(), name() };
        else if (vm >= VMBytecodeStorePublicContextPath) Fields["PublicContextPath"] = str();
    }
}
