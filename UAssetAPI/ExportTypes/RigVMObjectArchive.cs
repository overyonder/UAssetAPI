using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.ExportTypes;

/// <summary>
/// RigVMCore/RigVMObjectArchive.cpp: the objects an FRigVMObjectArchive holds, such as a graph function's collapse node.
/// FRigVMObjectArchiveWriter writes each object under the root in full where it is first referenced (state 1: class path,
/// name, flags and outer path relative to the root, then the object's Serialize), any other object as its path, relative to
/// the root when it lies under it (state 2), and null as state 0. A name is its string the first time (state 0) and the
/// offset of that string after (state 1); a text is its string. The archive's custom versions replace the package's.
/// Objects come out as a table that references index from 1, with outside paths at negative indices, as in a package.
/// </summary>
public sealed class RigVMObjectArchiveReader : AssetBinaryReader
{
    public readonly List<Dictionary<string, object>> Objects = new();
    public readonly List<string> Paths = new();
    readonly Dictionary<string, int> local = new();
    readonly Dictionary<long, FName> names = new();

    RigVMObjectArchiveReader(byte[] buffer, UAsset asset) : base(new MemoryStream(buffer, false), asset) { }

    public override bool PackageIndices => false;
    public override bool TextAsString => true;

    public override FName ReadFName()
    {
        byte state = ReadByte();
        if (state == 0) return NameAt(BaseStream.Position);
        if (state != 1) throw new FormatException($"Invalid name state {state} at {BaseStream.Position - 1}");
        long offset = ReadInt64();
        if (names.TryGetValue(offset, out var known)) return known;
        if (offset < 0 || offset >= BaseStream.Length) throw new FormatException($"Invalid name offset {offset}");
        long back = BaseStream.Position;
        BaseStream.Position = offset;
        var name = NameAt(offset);
        BaseStream.Position = back;
        return name;
    }

    FName NameAt(long offset)
    {
        string s = ReadFString()?.Value;
        return names[offset] = FName.DefineDummy(Asset, string.IsNullOrEmpty(s) ? "None" : s);
    }

    public override int ReadObjectIndex()
    {
        byte state = ReadByte();
        switch (state)
        {
            case 0: return 0;
            case 1: return ReadObject() + 1;
            case 2:
            {
                string path = ReadFName().ToString();
                if (local.TryGetValue(path == "None" ? "" : path, out int i)) return i + 1;
                Paths.Add(path);
                return -Paths.Count;
            }
            default: throw new FormatException($"Invalid object state {state} at {BaseStream.Position - 1}");
        }
    }

    int ReadObject()
    {
        string cls = ReadFName().ToString(), name = ReadFName().ToString();
        var flags = (EObjectFlags)ReadInt32();
        string outer = ReadFName().ToString();
        if (outer == "None") outer = "";
        int index = Objects.Count;
        var o = new Dictionary<string, object> { ["Class"] = cls, ["Name"] = name, ["Flags"] = flags, ["Outer"] = outer };
        Objects.Add(o);
        local[index == 0 ? "" : outer + "." + name] = index;
        if (cls.EndsWith(".RigVMLink"))
        {
            // RigVMLink.cpp: a link writes its two pin paths and nothing of its parent's
            o["SourcePinPath"] = ReadFString()?.Value;
            o["TargetPinPath"] = ReadFString()?.Value;
            return index;
        }
        // Obj.cpp UObject::Serialize: the tagged properties, then whether an object GUID follows unless the object is a template
        var header = new FUnversionedHeader(this);
        if (!Asset.HasUnversionedProperties && Asset.ObjectVersionUE5 >= ObjectVersionUE5.PROPERTY_TAG_EXTENSION_AND_OVERRIDABLE_SERIALIZATION)
        {
            var control = (EClassSerializationControlExtension)ReadByte();
            o["SerializationControl"] = control;
            if (control.HasFlag(EClassSerializationControlExtension.OverridableSerializationInformation)) o["Operation"] = (EOverriddenPropertyOperation)ReadByte();
        }
        var properties = new List<PropertyData>();
        for (PropertyData p; (p = MainSerializer.Read(this, null, null, null, header, true)) != null;) properties.Add(p);
        o["Properties"] = properties;
        if ((flags & (EObjectFlags.RF_ArchetypeObject | EObjectFlags.RF_ClassDefaultObject)) == 0 && ReadBooleanInt()) o["ObjectGuid"] = ReadGuid();
        return index;
    }

    /// <summary>Reads an FRigVMObjectArchive's buffer with its custom versions, decompressing it first when it is compressed.</summary>
    public static Dictionary<string, object> Decode(UAsset asset, List<object[]> versions, byte[] buffer, int uncompressedSize, int compressedSize, bool compressed)
    {
        var d = new Dictionary<string, object> { ["Versions"] = versions, ["UncompressedSize"] = uncompressedSize, ["CompressedSize"] = compressedSize, ["bIsCompressed"] = compressed };
        if (buffer.Length == 0) return d;
        if (compressed)
        {
            // FCompression with NAME_Zlib: a zlib stream
            var output = new byte[uncompressedSize];
            using var z = new ZLibStream(new MemoryStream(buffer, false), CompressionMode.Decompress);
            z.ReadExactly(output);
            if (z.ReadByte() != -1)
                asset.Coverage.Report("discarded-decompressed-data", $"RigVM object archive produces more than its declared {uncompressedSize} bytes; excess decompressed data is not retained.");
            buffer = output;
        }
        var saved = asset.CustomVersionContainer;
        if (versions != null)
        {
            var merged = saved.Where(v => !versions.Any(p => (Guid)p[0] == v.Key)).ToList();
            merged.AddRange(versions.Select(p => new CustomVersion((Guid)p[0], (int)p[1])));
            asset.CustomVersionContainer = merged;
        }
        try
        {
            using var r = new RigVMObjectArchiveReader(buffer, asset);
            if (r.ReadObjectIndex() != 1) throw new FormatException("The archive does not begin with its root object");
            if (r.BaseStream.Position != buffer.Length) throw new FormatException($"Object archive read {r.BaseStream.Position} of {buffer.Length} bytes");
            d["Objects"] = r.Objects;
            d["Paths"] = r.Paths;
        }
        finally { asset.CustomVersionContainer = saved; }
        return d;
    }
}
