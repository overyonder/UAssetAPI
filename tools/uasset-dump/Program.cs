// Dumps an Unreal package (.uasset/.umap) to UAssetAPI's JSON, including exports, properties and Kismet bytecode.
// Usage: uasset-dump <package> [engine version, default VER_UE5_8]
//        uasset-dump --check <package>...   parse each package, report raw (undecoded) exports and byte-exact round trips
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

// UASSET_USMAP names a .usmap whose native schemas resolve types that older property tags leave out
// UASSET_PARENTS names a TSV of project C++ class and parent, read from their UCLASS declarations, for classes no
// mappings describe
// UASSET_ENGINE names the Engine directory: /Engine content and each engine plugin's content, by its mount point (the
// .uplugin's name), so Blueprint classes defined there resolve to their native base
if (Environment.GetEnvironmentVariable("UASSET_ENGINE") is { Length: > 0 } engineDir)
{
    UAsset.MountPoints["Engine"] = Path.Combine(engineDir, "Content");
    foreach (var plugin in Directory.EnumerateFiles(Path.Combine(engineDir, "Plugins"), "*.uplugin", SearchOption.AllDirectories))
    {
        string content = Path.Combine(Path.GetDirectoryName(plugin), "Content");
        if (Directory.Exists(content)) UAsset.MountPoints.TryAdd(Path.GetFileNameWithoutExtension(plugin), content);
    }
}
if (Environment.GetEnvironmentVariable("UASSET_PARENTS") is { Length: > 0 } parents)
    foreach (var line in File.ReadLines(parents)) { var f = line.Split('\t'); if (f.Length == 2) NativeClassTails.ProjectNativeParents.TryAdd(f[0], f[1]); }
// Several can be given, separated by ':'; the first one that defines a schema or enum wins.
Usmap mappings = null;
foreach (var m in (Environment.GetEnvironmentVariable("UASSET_USMAP") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
{
    var next = new Usmap(m);
    if (mappings == null) { mappings = next; continue; }
    foreach (var kv in next.Schemas) if (!mappings.Schemas.ContainsKey(kv.Key)) mappings.Schemas[kv.Key] = kv.Value;
    foreach (var kv in next.EnumMap) if (!mappings.EnumMap.ContainsKey(kv.Key)) mappings.EnumMap[kv.Key] = kv.Value;
}

if (args[0] == "--trace") { try { _ = new UAsset(args[1], EngineVersion.UNKNOWN, mappings); Console.WriteLine("ok"); } catch (Exception ex) { Console.WriteLine(ex.ToString()); } return; }
if (args[0] == "--failures") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); foreach (var f in a.ExportParseFailures) Console.WriteLine($"{f.ObjectName}\t{f.ClassType}\n{f.ExceptionDetails}"); return; }
if (args[0] == "--check")
{
    foreach (var path in args.Skip(1))
    {
        try
        {
            var asset = new UAsset(path, EngineVersion.UNKNOWN, mappings);
            int raw = asset.Exports.Count(e => e is RawExport && !(e is RigHierarchyExport rh && rh.FullyParsed) && !(e is RigVMExport v && v.FullyParsed));
            var original = File.ReadAllBytes(path);
            var written = asset.WriteData().ToArray();
            bool same = original.AsSpan(0, Math.Min(original.Length, written.Length)).SequenceEqual(written) && original.Length == written.Length;
            int diff = 0; while (diff < Math.Min(original.Length, written.Length) && original[diff] == written[diff]) diff++;
            var rawClasses = string.Join(",", asset.Exports.OfType<RawExport>().Where(e => !(e is RigHierarchyExport rh && rh.FullyParsed) && !(e is RigVMExport v && v.FullyParsed)).Select(e => e.GetExportClassType().Value.Value).Distinct());
            Console.WriteLine($"{(raw == 0 && same ? "ok" : "PARTIAL")}\traw={raw}/{asset.Exports.Count}\troundtrip={(same ? "exact" : $"differs@{diff} len {original.Length}->{written.Length}")}\t{rawClasses}\t{path}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL\t{ex.GetType().Name}: {ex.Message.Split('\n')[0]}\t{path}");
        }
    }
    return;
}

// --classes <package>...: header only; one line per export: class name, package. Exports aren't parsed.
if (args[0] == "--classes")
{
    foreach (var path in args.Skip(1))
    {
        try
        {
            var asset = new UAsset(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming);
            foreach (var cls in asset.Exports.Select(e => e.GetExportClassType()?.Value?.Value ?? "?").Distinct()) Console.WriteLine($"{cls}\t{path}");
        }
        catch (Exception ex) { Console.WriteLine($"!fail:{ex.GetType().Name}: {ex.Message.Split('\n')[0]}\t{path}"); }
    }
    return;
}

// --audit <package>...: one line per undecoded part: raw export, export with leftover Extras bytes, or a property
// UAssetAPI could not type. Columns: kind, class (export class or property type), bytes, export name, package.
if (args[0] == "--audit")
{
    foreach (var path in args.Skip(1))
    {
        UAsset asset;
        try { asset = new UAsset(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); }
        catch (Exception ex) { Console.WriteLine($"fail\t{ex.GetType().Name}\t0\t{ex.Message.Split('\n')[0]}\t{path}"); continue; }
        for (int ei = 0; ei < asset.Exports.Count; ei++)
        {
            var e = asset.Exports[ei];
            // a class defined in this package (a Blueprint's generated class) is named by its export
            var cls = e.ClassIndex.IsExport() ? e.ClassIndex.ToExport(asset).ObjectName.ToString() : e.GetExportClassType()?.Value?.Value ?? "?";
            var name = e.ObjectName?.ToString() ?? "?";
            // a rig hierarchy keeps its bytes for writing; it counts as decoded when its parse read all of them
            if (e is RigHierarchyExport rh)
            {
                if (!rh.FullyParsed) Console.WriteLine($"rigparse\t{cls}\t{rh.Data?.Length ?? 0}\t{name} [{rh.ParseFailure}]\t{path}");
                else foreach (var c in rh.Components ?? new()) if (c.Payload.Length > 0) Console.WriteLine($"rigcomponent\t{c.ScriptStruct}\t{c.Payload.Length}\t{name}.{c.Name}\t{path}");
                continue;
            }
            if (e is RigVMExport vm)
            {
                if (!vm.FullyParsed) Console.WriteLine($"rigparse\t{cls}\t{vm.Data?.Length ?? 0}\t{name} [{vm.ParseFailure}]\t{path}");
                else if (vm.VM.LegacyDiscardedOnLoad) Console.WriteLine($"vmdiscarded\t{cls}\t{vm.Data?.Length ?? 0}\t{name}\t{path}");
                continue;
            }
            if (e is RawExport r)
            {
                var why = asset.ExportParseFailures.FirstOrDefault(f => f.ExportIndex == ei);
                string reason = why == null ? "" : $" [{why.ExceptionType?.Split('.').Last()}: {why.ExceptionMessage?.Split('\n')[0]}]";
                Console.WriteLine($"raw\t{cls}\t{r.Data?.Length ?? 0}\t{name}{reason}\t{path}");
                // UASSET_DETAILS prints each raw export's whole exception chain
                if (why != null && Environment.GetEnvironmentVariable("UASSET_DETAILS") != null) Console.WriteLine(why.ExceptionDetails);
                continue;
            }
            // a class default object loads through UClass::SerializeDefaultObject, which reads only its tagged properties, so
            // bytes after them never reach the engine: "cdotrail" keeps them apart from undecoded data the engine does read
            if (e.Extras != null && e.Extras.Length > 0) Console.WriteLine($"{(e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject) ? "cdotrail" : "extras")}\t{cls}\t{e.Extras.Length}\t{name}{(e.NativeTailFailure == null ? "" : $" [{e.NativeTailFailure}]")}\t{path}");
            if (e is NormalExport n) foreach (var p in n.Data) Walk(p, cls, name, path);
        }
        Console.WriteLine($"done\t-\t{asset.Exports.Count}\t-\t{path}");
    }
    return;
}

static void Walk(object o, string cls, string name, string path)
{
    switch (o)
    {
        case UAssetAPI.PropertyTypes.Objects.UnknownPropertyData u:
            Console.WriteLine($"rawprop\t{u.SerializingPropertyType?.Value ?? "?"}\t{u.Value?.Length ?? 0}\t{name}.{u.Name?.Value?.Value}\t{path}"); return;
        case UAssetAPI.PropertyTypes.Structs.RawStructPropertyData rs:
            Console.WriteLine($"rawstruct\t{rs.StructType?.Value?.Value ?? "?"}\t{rs.Value?.Length ?? 0}\t{name}.{rs.Name?.Value?.Value}{(rs.FailureReason == null ? "" : $" [{rs.FailureReason}]")}\t{path}"); return;
        case UAssetAPI.PropertyTypes.Objects.PropertyData pd:
            var v = pd.GetType().GetProperty("Value")?.GetValue(pd) ?? pd.GetType().GetField("Value")?.GetValue(pd);
            if (v is UAssetAPI.PropertyTypes.Objects.PropertyData) Walk(v, cls, name, path);
            else if (v is System.Collections.IDictionary d) { foreach (System.Collections.DictionaryEntry kv in d) { Walk(kv.Key, cls, name, path); Walk(kv.Value, cls, name, path); } }
            else if (v is System.Collections.IEnumerable en && v is not string && v is not byte[]) foreach (var x in en) Walk(x, cls, name, path);
            return;
    }
}

// --scriptspan <package>...: per class default object, the export map's serial size and script serialization start and end
if (args[0] == "--scriptspan") { foreach (var path in args.Skip(1)) { var a = new UAsset(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var e in a.Exports.Where(e => e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))) Console.WriteLine($"{e.SerialSize}\t{e.ScriptSerializationStartOffset}\t{e.ScriptSerializationEndOffset}\t{e.ObjectName}\t{path}"); } return; }
// --enum <type>: every member of a UAssetAPI enum with its compiled value, for comparison with the engine's own lists
if (args[0] == "--enum") { var t = typeof(UAsset).Assembly.GetTypes().First(t => t.Name == args[1]); foreach (var v in Enum.GetValues(t)) Console.WriteLine($"{Convert.ToInt64(v)} {v}"); return; }
// --versions <package>: object versions, flags and every custom version the package records
if (args[0] == "--versions") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); Console.WriteLine($"legacy {a.LegacyFileVersion}\tUE4 {a.ObjectVersion}\tUE5 {a.ObjectVersionUE5}\tflags {a.PackageFlags}\tfilterEditorOnly {a.IsFilterEditorOnly}\tunversioned {a.HasUnversionedProperties}"); foreach (var c in a.CustomVersionContainer) Console.WriteLine($"{c.FriendlyName ?? c.Key.ToString()}\t{c.Version}"); return; }
// --cdos <package>...: per class default object, its decoded type, leftover bytes, and the native tails decoded after its properties
if (args[0] == "--cdos") { foreach (var path in args.Skip(1)) { try { var a = new UAsset(path, EngineVersion.UNKNOWN, mappings); foreach (var e in a.Exports.Where(e => e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))) Console.WriteLine($"{e.GetType().Name}\t{e.Extras?.Length ?? -1}\t{(e.Extras == null ? "" : Convert.ToHexString(e.Extras))}\t{e.NativeTails?.Tails.ToString() ?? "-"}\t{e.ObjectName}\t{path}"); } catch (Exception ex) { Console.WriteLine($"fail\t{ex.Message.Split('\n')[0]}\t{path}"); } } return; }
// --exports <package>: index, decoded type, class, name and serial size of every export
// --names <package>: the name map, one entry per line
// --registry: every struct type the decoder reads with its own serializer
if (args[0] == "--registry")
{
    var t = typeof(UAsset).Assembly.GetTypes().First(t => t.Name == "MainSerializer");
    var reg = (System.Collections.IDictionary)t.GetProperty("PropertyTypeRegistry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
    foreach (System.Collections.DictionaryEntry e in reg)
    {
        var custom = e.Value.GetType().GetField("HasCustomStructSerialization", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(e.Value);
        Console.WriteLine($"{e.Key}\t{custom}");
    }
    return;
}
// --usage <package list> <struct names>: header only, every imported class (module, name) and every name-map entry that is one of
// the given struct names, each with the number of packages using it and one example; packages that fail to open are listed
if (args[0] == "--usage")
{
    var interest = new HashSet<string>(File.ReadLines(args[2]).Where(l => l.Length > 0));
    var counts = new Dictionary<(string Kind, string Module, string Name), (int Count, string Example)>();
    void Add((string, string, string) key, string path) => counts[key] = counts.TryGetValue(key, out var v) ? (v.Count + 1, v.Example) : (1, path);
    int done = 0;
    foreach (var path in File.ReadLines(args[1]))
    {
        if (path.Length == 0) continue;
        UAsset a;
        try { a = new UAsset(path, EngineVersion.UNKNOWN, null, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); }
        catch (Exception ex) { Console.WriteLine($"fail\t{ex.GetType().Name}\t{ex.Message.Split('\n')[0]}\t{path}"); continue; }
        var seen = new HashSet<(string, string, string)>();
        foreach (var imp in a.Imports)
        {
            string cls = imp.ClassName?.ToString();
            if (cls != "Class" && cls != "ScriptStruct") continue;
            string module = imp.OuterIndex.IsImport() ? imp.OuterIndex.ToImport(a).ObjectName?.ToString() : "";
            seen.Add((cls == "Class" ? "class" : "struct-import", module, imp.ObjectName?.ToString()));
        }
        foreach (var n in a.GetNameMapIndexList())
            if (interest.Contains(n.Value)) seen.Add(("struct-name", "", n.Value));
        foreach (var k in seen) Add(k, path);
        if (++done % 10000 == 0) Console.Error.WriteLine($"{done} packages");
    }
    foreach (var (k, v) in counts.OrderBy(kv => kv.Key)) Console.WriteLine($"{k.Kind}\t{k.Module}\t{k.Name}\t{v.Count}\t{v.Example}");
    return;
}
if (args[0] == "--names") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var n in a.GetNameMapIndexList()) Console.WriteLine(n.Value); return; }
if (args[0] == "--extras") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings); foreach (var e in a.Exports.Where(e => args.Length < 3 || e.ObjectName.ToString() == args[2])) if (e.Extras?.Length > 0) Console.WriteLine($"{e.ObjectName}\t{e.GetType().Name}\t{e.SerialOffset}\t{e.SerialSize}\t{e.ScriptSerializationStartOffset}\t{e.ScriptSerializationEndOffset}\t{e.NativeTailFailure}\t{Convert.ToHexString(e.Extras.Length > 64 && args.Length < 4 ? e.Extras[..64] : e.Extras)}"); return; }
if (args[0] == "--exports") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings); for (int i = 0; i < a.Exports.Count; i++) { var e = a.Exports[i]; Console.WriteLine($"{i}\t{e.GetType().Name}\t{e.GetExportClassType()?.Value?.Value}\t{e.ObjectName}\t{e.SerialSize}"); } return; }
if (args[0] == "--sections") { var a = new UAsset(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var fi in typeof(UAsset).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(f => f.Name.EndsWith("Offset") || f.Name.EndsWith("Count"))) Console.WriteLine($"{fi.Name}\t{fi.GetValue(a)}"); return; }
if (args[0] == "--write") { File.WriteAllBytes(args[2], new UAsset(args[1], EngineVersion.UNKNOWN, mappings).WriteData().ToArray()); return; }

var version = args.Length > 1 ? Enum.Parse<EngineVersion>(args[1]) : EngineVersion.UNKNOWN;
Console.Out.Write(new UAsset(args[0], version, mappings).SerializeJson(true));
