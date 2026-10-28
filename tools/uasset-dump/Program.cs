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

using var coverageSummary = new CoverageWarningSummary();
using var diagnosticOutput = new CoverageDiagnosticOutput(coverageSummary.Warnings,
    args.Length > 0 && args[0] is "--trace" or "--failures" or "--classes" or "--scriptspan"
        or "--versions" or "--cdos" or "--having" or "--usage" or "--names" or "--extras"
        or "--exports" or "--sections" or "--check" or "--audit" or "--audit-iostore-index" or "--dataset" or "--annotate-index" or "--merge-diagnostics");
try
{

// Merge worker diagnostics without duplicating their headers inside the combined body.
// Warning counts come from worker headers, which include formatter findings as well as audit records.
if (args.Length > 0 && args[0] == "--merge-diagnostics")
{
    bool fromRecords = args.Length > 1 && args[1] == "--records";
    foreach (string source in args.Skip(fromRecords ? 2 : 1))
        foreach (string line in File.ReadLines(source))
        {
            if (fromRecords)
            {
                if (line.StartsWith('#')) continue;
                int separator = line.IndexOf('\t');
                if (separator > 0 && line[..separator] is not ("index" or "done"))
                    coverageSummary.Warnings.WriteLine(line);
                Console.WriteLine(line);
                continue;
            }
            if (line == "# WARNING: " + CoverageWarnings.Notice) continue;
            if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                int close = line.IndexOf("): ", StringComparison.Ordinal);
                int open = close < 0 ? -1 : line.LastIndexOf(" (", close, StringComparison.Ordinal);
                if (open < 2 || !long.TryParse(line.AsSpan(open + 2, close - open - 2), out long count) || count <= 0)
                    throw new InvalidDataException("Invalid worker coverage header in " + source + ": " + line);
                coverageSummary.Warnings.Add(line[2..open], line[(close + 3)..], count);
            }
            else Console.WriteLine(line);
        }
    return;
}

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
// UASSET_MOUNTS names a list of .uplugin paths inside projects; each mounts its Content under the plugin's name for
// packages of the project that holds it (the directory above its Plugins directory)
if (Environment.GetEnvironmentVariable("UASSET_MOUNTS") is { Length: > 0 } mounts)
    foreach (var plugin in File.ReadLines(mounts))
    {
        int at = plugin.IndexOf("/Plugins/");
        if (at < 0) continue;
        string project = plugin.Substring(0, at);
        if (!UAsset.ProjectMountPoints.TryGetValue(project, out var table)) UAsset.ProjectMountPoints[project] = table = new(StringComparer.OrdinalIgnoreCase);
        table.TryAdd(Path.GetFileNameWithoutExtension(plugin), Path.Combine(Path.GetDirectoryName(plugin), "Content"));
    }
if (Environment.GetEnvironmentVariable("UASSET_PARENTS") is { Length: > 0 } parents)
    foreach (var line in File.ReadLines(parents)) { var f = line.Split('\t'); if (f.Length == 2) NativeClassTails.ProjectNativeParents.TryAdd(f[0], f[1]); }
string serializerCatalog = Environment.GetEnvironmentVariable("UASSET_SERIALIZERS")
    ?? Path.Combine(AppContext.BaseDirectory, "project-serializers.tsv");
if (File.Exists(serializerCatalog))
    foreach (string line in File.ReadLines(serializerCatalog))
    {
        var fields = line.Split('\t');
        if (fields.Length != 3 || !byte.TryParse(fields[2], out byte flags) || (flags & ~7) != 0)
            throw new InvalidDataException("Invalid native serialization catalog entry: " + line);
        NativeClassTails.RegisterProjectSerialization(fields[0], fields[1], (flags & 1) != 0, (flags & 2) != 0, (flags & 4) == 0);
    }
string structSerializerCatalog = Environment.GetEnvironmentVariable("UASSET_STRUCT_SERIALIZERS")
    ?? Path.Combine(AppContext.BaseDirectory, "project-struct-serializers.tsv");
if (File.Exists(structSerializerCatalog))
    foreach (string line in File.ReadLines(structSerializerCatalog))
    {
        string[] fields = line.Split('\t');
        if (fields.Length != 3 || fields[2] is not ("0" or "1" or "2"))
            throw new InvalidDataException("Invalid native struct serialization catalog entry: " + line);
        UAssetAPI.PropertyTypes.Structs.NativeStructSerialization.RegisterProjectSerialization(fields[0], fields[1], fields[2] == "1", fields[2] != "2");
    }
// UASSET_MAPS names TSVs, separated by ':', of owning type, property name, key struct and value struct for map
// properties no mappings describe: project-maps.tsv from the project headers, and removed-maps.tsv for engine
// properties a later release removed, whose types the property tag and the data that follows establish
if (Environment.GetEnvironmentVariable("UASSET_MAPS") is { Length: > 0 } maps)
    foreach (var line in maps.Split(':', StringSplitOptions.RemoveEmptyEntries).SelectMany(File.ReadLines)) { var f = line.Split('\t'); if (f.Length == 4) UAssetAPI.PropertyTypes.Objects.MapPropertyData.AddProjectMapStructTypes(f[0], f[1], f[2], f[3]); }
// Several can be given, separated by ':'; the first one that defines a schema or enum wins.
Usmap mappings = null;
// Each top-level package starts a fresh schema scope, so one package's types never stand in for another project's
UAsset Load(string path, EngineVersion version, Usmap map, CustomSerializationFlags flags = CustomSerializationFlags.None)
{
    map?.BeginAssetScope();
    try
    {
        var asset = new UAsset(path, version, map, flags | CustomSerializationFlags.TrackReadCoverage);
        // Diagnostic commands also need the decoder's findings in the invocation's final summary.
        if (args[0] is "--trace" or "--failures" or "--classes" or "--scriptspan" or "--versions"
            or "--cdos" or "--having" or "--usage" or "--names" or "--extras" or "--exports" or "--sections" or "--write")
        {
            InspectCoverage(asset, path);
            if (args[0] != "--write")
                coverageSummary.Warnings.Add("diagnostic output omits package fields", $"{path}: {args[0]} excludes the package's FolderName summary field from its diagnostic output");
        }
        return asset;
    }
    catch (Exception ex)
    {
        coverageSummary.Warnings.Add("package load failed", path + ": " + ex.Message.Split('\n')[0]);
        throw;
    }
}
foreach (var m in (Environment.GetEnvironmentVariable("UASSET_USMAP") ?? "").Split(':', StringSplitOptions.RemoveEmptyEntries))
{
    var next = new Usmap(m);
    if (mappings == null) { mappings = next; continue; }
    foreach (var kv in next.Schemas) if (!mappings.Schemas.ContainsKey(kv.Key)) mappings.Schemas[kv.Key] = kv.Value;
    foreach (var kv in next.EnumMap) if (!mappings.EnumMap.ContainsKey(kv.Key)) mappings.EnumMap[kv.Key] = kv.Value;
}

// export-table fields and the tagged properties --text already prints (including a default object's sparse class data)
HashSet<string> Headers = new() { "ObjectName", "OuterIndex", "ClassIndex", "SuperIndex", "TemplateIndex", "ObjectFlags", "SerialSize", "SerialOffset", "ScriptSerializationStartOffset", "ScriptSerializationEndOffset", "bForcedExport", "bNotForClient", "bNotForServer", "PackageGuid", "IsInheritedInstance", "PackageFlags", "bNotAlwaysLoadedForEditorGame", "bIsAsset", "GeneratePublicHash", "SerializationBeforeSerializationDependencies", "CreateBeforeSerializationDependencies", "SerializationBeforeCreateDependencies", "CreateBeforeCreateDependencies", "PublicExportHash", "Padding", "Extras", "SerializationControl", "Operation", "HasLeadingFourNullBytes", "HasNoTaggedProperties", "SparseClassDataStruct", "SparseClassData" };
if (args[0] == "--trace") { try { _ = Load(args[1], EngineVersion.UNKNOWN, mappings); Console.WriteLine("ok"); } catch (Exception ex) { Console.WriteLine(ex.ToString()); } return; }
if (args[0] == "--failures") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); foreach (var f in a.ExportParseFailures) Console.WriteLine($"{f.ObjectName}\t{f.ClassType}\n{f.ExceptionDetails}"); return; }
// --json <package> <out.json>: every decoded export, for reading a reference asset's graphs, tables and settings
if (args[0] == "--json") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); var warnings = InspectCoverage(a, args[1]); WriteJson(a, args[2], warnings); return; }
if (args[0] == "--text") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); var warnings = InspectCoverage(a, args[1]); warnings.WriteFile(args[2], w => { TextDump.Write(a, w, warnings); return true; }); return; }
// --native <package> <out> [export]: per export, everything decoded outside its tagged properties (native class tails,
// specialized export data, bytecode), as UAssetAPI's JSON without the tagged Data that --text prints
if (args[0] == "--native")
{
    var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming);
    var warnings = InspectCoverage(a, args[1]);
    if (args.Length >= 4 && a.Exports.Count > 1) warnings.Add("selected export", "Only one of the package's exports is included.");
    warnings.WriteFile(args[2], w => WriteNative(a, w, args.Length >= 4 ? args[3] : null, new PayloadSidecar(null, null, warnings), warnings));
    return;
}
if (args[0] == "--check")
{
    foreach (var path in args.Skip(1))
    {
        try
        {
            var asset = Load(path, EngineVersion.UNKNOWN, mappings);
            InspectCoverage(asset, path);
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
            coverageSummary.Warnings.Add("failure", path + ": " + ex.Message.Split('\n')[0]);
            Console.WriteLine($"FAIL\t{ex.GetType().Name}: {ex.Message.Split('\n')[0]}\t{path}");
        }
    }
    return;
}

// --classes <package>...: header only; one line per export class: class name, export count, package. Exports aren't parsed.
if (args[0] == "--classes")
{
    foreach (var path in args.Skip(1))
    {
        try
        {
            var asset = Load(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming);
            foreach (var g in asset.Exports.GroupBy(e => e.GetExportClassType()?.Value?.Value ?? "?")) Console.WriteLine($"{g.Key}\t{g.Count()}\t{path}");
        }
        catch (Exception ex) { Console.WriteLine($"!fail:{ex.GetType().Name}: {ex.Message.Split('\n')[0]}\t{path}"); }
    }
    return;
}

// --audit <package>...: one line per undecoded part: raw export, export with leftover Extras bytes, or a property
// UAssetAPI could not type. Columns: kind, class (export class or property type), bytes, export name, package.
// UnrealPak -ListContainer writes this CSV directly from its IoStore directory and chunk index.
// This API has no IoStore reader: account for those required inputs explicitly instead of excluding the containers.
if (args[0] == "--audit-iostore-index")
{
    using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(args[1])
    {
        TextFieldType = Microsoft.VisualBasic.FileIO.FieldType.Delimited,
        HasFieldsEnclosedInQuotes = true,
        TrimWhiteSpace = true,
    };
    parser.SetDelimiters(",");
    string[] headers = parser.ReadFields() ?? throw new InvalidDataException("Missing IoStore index header");
    int chunkId = Array.IndexOf(headers, "ChunkId"), chunkType = Array.IndexOf(headers, "ChunkType"),
        filename = Array.IndexOf(headers, "Filename"), size = Array.IndexOf(headers, "Size");
    if (chunkId < 0 || chunkType < 0 || filename < 0 || size < 0)
        throw new InvalidDataException("IoStore index lacks a required column");
    var warnings = new CoverageWarnings(Console.Out, coverageSummary.Warnings);
    while (!parser.EndOfData)
    {
        string[] fields = parser.ReadFields();
        if (fields == null || fields.Length != headers.Length || !long.TryParse(fields[size], out long length) || length < 0)
            throw new InvalidDataException("Invalid IoStore index row");
        warnings.WriteLine($"unsupported-input\t{fields[chunkType]}\t{length}\t{fields[chunkId]} {fields[filename]}: IoStore chunk not decoded; this API reads conventional package files\t{args[2]}");
    }
    return;
}
if (args[0] == "--audit")
{
    foreach (var path in args.Skip(1))
    {
        UAsset asset;
        try { asset = Load(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); }
        catch (Exception ex) { coverageSummary.Warnings.Add("failure", path + ": " + ex.Message.Split('\n')[0]); Console.WriteLine($"fail\t{ex.GetType().Name}\t0\t{ex.Message.Split('\n')[0]}\t{path}"); continue; }
        InspectCoverage(asset, path, Console.Out);
        Console.WriteLine($"done\t-\t{asset.Exports.Count}\t-\t{path}");
    }
    return;
}

// Summarize the actual saved output set after all dataset workers/retries finish.
if (args[0] == "--coverage-summary")
{
    var missing = new HashSet<string>(File.ReadLines(args[3]).Where(line => line.Length > 0), StringComparer.Ordinal);
    long labelled = 0, absent = 0;
    foreach (string line in File.ReadLines(args[1]))
    {
        var fields = line.Split('\t');
        if (fields.Length < 3) throw new InvalidDataException("Invalid index row");
        missing.Remove(fields[0]);
        foreach (string field in fields.Skip(1).Take(2).Where(field => Path.GetFileName(field) != "-"))
        {
            string file = Path.Combine(args[2], field);
            if (!File.Exists(file)) { absent++; continue; }
            if (CoverageAnnotations.HasWarning(file)) labelled++;
        }
    }
    if (missing.Count > 0) coverageSummary.Warnings.Add("dataset omission", $"{missing.Count} input package paths have no exported index row");
    if (absent > 0) coverageSummary.Warnings.Add("missing output files", $"{absent} indexed output files do not exist");
    if (labelled > 0) coverageSummary.Warnings.Add("affected output files", $"{labelled} saved files contain concrete coverage warnings");
    return;
}

// --annotate-index <index.tsv> <output root> [saved residue.tsv]: audit sources and label existing bodies only.
if (args[0] == "--annotate-index")
{
    var saved = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    if (args.Length > 3)
        foreach (string line in File.ReadLines(args[3]))
        {
            var fields = line.Split('\t');
            if (fields.Length < 5 || fields[0] is "done" or "index" or "fail") continue;
            if (!saved.TryGetValue(fields[^1], out var rows)) saved[fields[^1]] = rows = new();
            rows.Add(line);
        }
    int checkedCount = 0, changed = 0, failures = 0;
    long nextProgress = Environment.TickCount64 + 10000;
    object parseGate = new(); // Mapping scopes and native-parent caches belong to one package at a time.
    var lines = System.Collections.Concurrent.Partitioner.Create(File.ReadLines(args[1]), System.Collections.Concurrent.EnumerablePartitionerOptions.NoBuffering);
    System.Threading.Tasks.Parallel.ForEach(lines, new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 16 }, line =>
    {
        var fields = line.Split('\t');
        try
        {
            if (fields.Length < 3) throw new InvalidDataException("Invalid index row");
            var outputs = fields.Skip(1).Take(2).Where(field => Path.GetFileName(field) != "-")
                .Select(field => Path.GetFullPath(Path.Combine(args[2], field))).ToArray();
            if (outputs.Any(path => !path.StartsWith(Path.GetFullPath(args[2]) + "/", StringComparison.Ordinal))) throw new InvalidDataException("Output outside root");
            if (outputs.Length > 0 && outputs.All(CoverageAnnotations.HasWarning))
                coverageSummary.Warnings.Add("existing concrete warning", outputs[0]);
            else
            {
                var textWarnings = new CoverageWarnings(summary: coverageSummary.Warnings);
                foreach (string output in outputs.Where(path => path.EndsWith(".txt", StringComparison.Ordinal)))
                    if (SavedCoverageInspector.FindTextOmission(output, textWarnings)) break;
                var nativeWarnings = new CoverageWarnings(summary: coverageSummary.Warnings);
                foreach (string output in outputs)
                    if (SavedCoverageInspector.FindNativeOmission(output, nativeWarnings)) break;
                if (outputs.All(path => CoverageAnnotations.HasWarning(path) || (path.EndsWith(".txt", StringComparison.Ordinal) ? textWarnings.Any : path.EndsWith(".native.json", StringComparison.Ordinal) && nativeWarnings.Any)))
                {
                    foreach (string path in outputs.Where(path => !CoverageAnnotations.HasWarning(path)))
                        if (CoverageAnnotations.Apply(path, path.EndsWith(".txt", StringComparison.Ordinal) ? textWarnings : nativeWarnings)) System.Threading.Interlocked.Increment(ref changed);
                    System.Threading.Interlocked.Increment(ref checkedCount);
                    return;
                }
                // One observed omission establishes that the label applies. Probe the summary before decoding exports.
                var asset = new UAsset(EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming);
                using var summaryReader = new AssetBinaryReader(File.OpenRead(fields[0]), asset);
                asset.ReadHeader(summaryReader);
                var warnings = new CoverageWarnings(summary: coverageSummary.Warnings);
                foreach (var diagnostic in asset.Coverage.Diagnostics)
                    warnings.Add(diagnostic.Kind, fields[0] + ": " + diagnostic.Detail);
                if (saved.TryGetValue(fields[0], out var rows)) foreach (var row in rows) warnings.WriteLine(row);
                if (!warnings.Any)
                {
                    asset.ReadThumbnails(summaryReader);
                    var image = asset.Thumbnails?.FirstOrDefault(pair => pair.Value.CompressedImageData?.Length > 0);
                    if (image.HasValue && image.Value.Value != null)
                        warnings.Add("opaque thumbnail payload", $"{fields[0]}: {image.Value.Key} retains {image.Value.Value.CompressedImageData.Length} compressed image bytes without decoding them");
                }
                if (!warnings.Any)
                    foreach (string output in outputs.Where(path => path.EndsWith(".json", StringComparison.Ordinal)))
                        if (SavedCoverageInspector.FindCollectionPruning(output, warnings)) break;
                if (!warnings.Any)
                {
                    lock (parseGate)
                    {
                        asset = Load(fields[0], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming);
                        warnings = InspectCoverage(asset, fields[0]);
                        if (!warnings.Any)
                        {
                            if (outputs.Any(path => path.EndsWith(".txt", StringComparison.Ordinal))) TextDump.Write(asset, TextWriter.Null, textWarnings);
                            if (outputs.Any(path => path.EndsWith(".native.json", StringComparison.Ordinal)))
                                foreach (var export in asset.Exports.OfType<NormalExport>().Where(export => export.Data?.Count > 0))
                                    nativeWarnings.Add("native tagged properties omitted", $"{export.ObjectName}: {export.Data.Count} tagged properties are excluded from the native view");
                        }
                        asset = null;
                    }
                }
                foreach (string path in outputs)
                    if (CoverageAnnotations.Apply(path, warnings.Any ? warnings : path.EndsWith(".txt", StringComparison.Ordinal) ? textWarnings : path.EndsWith(".native.json", StringComparison.Ordinal) ? nativeWarnings : warnings)) System.Threading.Interlocked.Increment(ref changed);
            }
            System.Threading.Interlocked.Increment(ref checkedCount);
        }
        catch (Exception ex)
        {
            System.Threading.Interlocked.Increment(ref failures);
            coverageSummary.Warnings.Add("annotation failed", fields[0] + ": " + ex.Message.Split('\n')[0]);
            Console.Error.WriteLine($"annotation-failed\t{fields[0]}\t{ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
        }
        long now = Environment.TickCount64, previous = System.Threading.Interlocked.Read(ref nextProgress);
        if (now >= previous && System.Threading.Interlocked.CompareExchange(ref nextProgress, now + 10000, previous) == previous)
        {
            Console.WriteLine($"Checked {checkedCount}; labelled {changed} files; failures {failures}");
            Console.Out.Flush();
        }
    });
    Console.WriteLine($"Finished: checked {checkedCount}; labelled {changed} files; failures {failures}");
    Environment.ExitCode = failures == 0 ? 0 : 1;
    return;
}

// --dataset <root> <out dir> <package>...: parses each package once and writes <out dir>/<path under root>.txt (--text)
// and .native.json (--native, when any export has native data); prints the --audit lines, then an index line: index,
// package, text file, native file or -, export count, residue count
if (args[0] == "--dataset")
{
    string root = Path.GetFullPath(args[1]), outDir = args[2];
    foreach (var path in args.Skip(3))
    {
        UAsset asset;
        try { asset = Load(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.ReadOnlyStreaming); }
        catch (Exception ex) { coverageSummary.Warnings.Add("failure", path + ": " + ex.Message.Split('\n')[0]); Console.WriteLine($"fail\t{ex.GetType().Name}\t0\t{ex.Message.Split('\n')[0]}\t{path}"); continue; }
        try
        {
            string rel = Path.GetRelativePath(root, Path.GetFullPath(path)), text = rel + ".txt", native = rel + ".native.json";
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(outDir, rel)));
            var warnings = InspectCoverage(asset, path, Console.Out);
            string textBody = Path.GetTempFileName(), nativeBody = Path.GetTempFileName();
            try
            {
                var textWarnings = warnings.Fork();
                var nativeWarnings = warnings.Fork();
                using (var w = new StreamWriter(textBody)) TextDump.Write(asset, w, textWarnings);
                bool any;
                using (var w = new StreamWriter(nativeBody)) any = WriteNative(asset, w, null, new PayloadSidecar(outDir, rel + ".payloads", nativeWarnings), nativeWarnings);
                textWarnings.WriteStagedBody(Path.Combine(outDir, text), textBody);
                if (any) nativeWarnings.WriteStagedBody(Path.Combine(outDir, native), nativeBody);
                else { File.Delete(Path.Combine(outDir, native)); native = "-"; }
            }
            finally { File.Delete(textBody); File.Delete(nativeBody); }
            int count = warnings.RecordCount;
            Console.WriteLine($"index\t{path}\t{text}\t{native}\t{asset.Exports.Count}\t{count}");
        }
        // a package that runs out of the worker's heap is retried alone with a larger one
        catch (Exception ex) { coverageSummary.Warnings.Add("failure", path + ": " + ex.Message.Split('\n')[0]); Console.WriteLine($"fail\t{ex.GetType().Name}\t0\t{ex.Message.Split('\n')[0]}\t{path}"); if (Environment.GetEnvironmentVariable("UASSET_DETAILS") != null) Console.Error.WriteLine(ex); }
        asset = null;
    }
    return;
}

// --scriptspan <package>...: per class default object, the export map's serial size and script serialization start and end
if (args[0] == "--scriptspan") { foreach (var path in args.Skip(1)) { var a = Load(path, EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var e in a.Exports.Where(e => e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))) Console.WriteLine($"{e.SerialSize}\t{e.ScriptSerializationStartOffset}\t{e.ScriptSerializationEndOffset}\t{e.ObjectName}\t{path}"); } return; }
// --enum <type>: every member of a UAssetAPI enum with its compiled value, for comparison with the engine's own lists
if (args[0] == "--enum") { var t = typeof(UAsset).Assembly.GetTypes().First(t => t.Name == args[1]); foreach (var v in Enum.GetValues(t)) Console.WriteLine($"{Convert.ToInt64(v)} {v}"); return; }
// --versions <package>: object versions, flags and every custom version the package records
if (args[0] == "--versions") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); Console.WriteLine($"legacy {a.LegacyFileVersion}\tUE4 {a.ObjectVersion}\tUE5 {a.ObjectVersionUE5}\tflags {a.PackageFlags}\tfilterEditorOnly {a.IsFilterEditorOnly}\tunversioned {a.HasUnversionedProperties}"); foreach (var c in a.CustomVersionContainer) Console.WriteLine($"{c.FriendlyName ?? c.Key.ToString()}\t{c.Version}"); return; }
// --cdos <package>...: per class default object, its decoded type, leftover bytes, and the native tails decoded after its properties
if (args[0] == "--cdos") { foreach (var path in args.Skip(1)) { try { var a = Load(path, EngineVersion.UNKNOWN, mappings); foreach (var e in a.Exports.Where(e => e.ObjectFlags.HasFlag(EObjectFlags.RF_ClassDefaultObject))) Console.WriteLine($"{e.GetType().Name}\t{e.Extras?.Length ?? -1}\t{(e.Extras == null ? "" : Convert.ToHexString(e.Extras))}\t{e.NativeTails?.Tails.ToString() ?? "-"}\t{e.ObjectName}\t{path}"); } catch (Exception ex) { Console.WriteLine($"fail\t{ex.Message.Split('\n')[0]}\t{path}"); } } return; }
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
// --having <package list> <names>: header only, each package whose name map holds one of the given names, with that name
if (args[0] == "--having")
{
    var wanted = new HashSet<string>(File.ReadLines(args[2]).Where(l => l.Length > 0));
    foreach (var path in File.ReadLines(args[1]))
    {
        if (path.Length == 0) continue;
        UAsset a;
        try { a = Load(path, EngineVersion.UNKNOWN, null, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); }
        catch (Exception ex) { Console.WriteLine($"fail\t{ex.Message.Split('\n')[0]}\t{path}"); continue; }
        foreach (var n in a.GetNameMapIndexList()) if (wanted.Contains(n.Value)) Console.WriteLine($"{n.Value}\t{path}");
    }
    return;
}
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
        try { a = Load(path, EngineVersion.UNKNOWN, null, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); }
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
if (args[0] == "--names") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var n in a.GetNameMapIndexList()) Console.WriteLine(n.Value); return; }
if (args[0] == "--extras") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings); foreach (var e in a.Exports.Where(e => args.Length < 3 || e.ObjectName.ToString() == args[2])) if (e.Extras?.Length > 0) Console.WriteLine($"{e.ObjectName}\t{e.GetType().Name}\t{e.SerialOffset}\t{e.SerialSize}\t{e.ScriptSerializationStartOffset}\t{e.ScriptSerializationEndOffset}\t{e.NativeTailFailure}\t{Convert.ToHexString(e.Extras.Length > 64 && args.Length < 4 ? e.Extras[..64] : e.Extras)}"); return; }
if (args[0] == "--exports") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings); for (int i = 0; i < a.Exports.Count; i++) { var e = a.Exports[i]; Console.WriteLine($"{i}\t{e.GetType().Name}\t{e.GetExportClassType()?.Value?.Value}\t{e.ObjectName}\t{e.SerialSize}"); } return; }
if (args[0] == "--sections") { var a = Load(args[1], EngineVersion.UNKNOWN, mappings, CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming); foreach (var fi in typeof(UAsset).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic).Where(f => f.Name.EndsWith("Offset") || f.Name.EndsWith("Count"))) Console.WriteLine($"{fi.Name}\t{fi.GetValue(a)}"); return; }
if (args[0] == "--write") { File.WriteAllBytes(args[2], Load(args[1], EngineVersion.UNKNOWN, mappings).WriteData().ToArray()); return; }

var version = args.Length > 1 ? Enum.Parse<EngineVersion>(args[1]) : EngineVersion.UNKNOWN;
var defaultAsset = Load(args[0], version, mappings);
WriteJson(defaultAsset, null, InspectCoverage(defaultAsset, args[0]));

void WriteJson(UAsset asset, string path, CoverageWarnings warnings)
{
    var nameOffset = typeof(UAsset).GetField("NameOffset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    warnings.Add("package summary member omitted", $"Package.NameOffset={nameOffset.GetValue(asset)}: the decoded source name-table offset is excluded from UAssetAPI's JSON contract");
    warnings.WriteFile(path, w =>
    {
        var serializer = UAsset.CreateJsonSerializer();
        asset.Info = "Serialized with " + UAPUtils.DisplayVersion;
        serializer.Converters.Insert(0, new PayloadSidecar(null, null, warnings));
        serializer.ContractResolver = new PayloadWarningResolver(serializer.ContractResolver, warnings);
        serializer.Serialize(new Newtonsoft.Json.JsonTextWriter(w) { Formatting = Newtonsoft.Json.Formatting.Indented, CloseOutput = false }, asset);
        return true;
    }, json: true);
}

CoverageWarnings InspectCoverage(UAsset asset, string path, TextWriter records = null)
{
    var warnings = new CoverageWarnings(records, coverageSummary.Warnings);
    new CoverageInspector(path, warnings).Inspect(asset);
    return warnings;
}

bool WriteNative(UAsset a, TextWriter w, string only, Newtonsoft.Json.JsonConverter payloads = null, CoverageWarnings warnings = null)
{
    warnings?.Add("native package summary omitted", $"PackageFlags={a.PackageFlags}: the package summary flags are excluded from the native view");
    // one compact JSON line per export after its header line; the tagged properties are --text's, so they're detached
    // rather than serialized and discarded
    var serializer = UAsset.CreateReadableJsonSerializer();
    if (payloads != null) serializer.Converters.Add(payloads);
    if (warnings != null) serializer.ContractResolver = new PayloadWarningResolver(serializer.ContractResolver, warnings);
    if (only != null && !a.Exports.Any(e => e.ObjectName.ToString() == only)) warnings?.Add("selected export missing", $"No export named {only} exists in the package.");
    bool any = false;
    for (int i = 0; i < a.Exports.Count; i++)
    {
        var e = a.Exports[i];
        if (only != null && e.ObjectName.ToString() != only) continue;
        var data = (e as NormalExport)?.Data;
        if (data?.Count > 0) warnings?.Add("native tagged properties omitted", $"{e.ObjectName}: {data.Count} tagged properties are excluded from the native view");
        if (e is NormalExport detach) detach.Data = null;
        // serialized to a temporary file and copied token by token, so a mesh's index arrays never become a JToken tree
        string tmp = Path.GetTempFileName();
        try
        {
            using (var tw = new StreamWriter(tmp)) try { serializer.Serialize(new Newtonsoft.Json.JsonTextWriter(tw) { Formatting = Newtonsoft.Json.Formatting.None }, e); }
                finally { if (e is NormalExport attach) attach.Data = data; }
            using var tr = new StreamReader(tmp);
            var r = new Newtonsoft.Json.JsonTextReader(tr) { FloatParseHandling = Newtonsoft.Json.FloatParseHandling.Double, DateParseHandling = Newtonsoft.Json.DateParseHandling.None, MaxDepth = null };
            Newtonsoft.Json.JsonTextWriter jw = null;
            if (!r.Read() || r.TokenType != Newtonsoft.Json.JsonToken.StartObject) throw new InvalidDataException($"Native serializer did not produce an object for {e.ObjectName}.");
            while (r.Read() && r.TokenType == Newtonsoft.Json.JsonToken.PropertyName)
            {
                string name = (string)r.Value;
                if (!r.Read()) throw new EndOfStreamException($"Native serializer ended before {e.ObjectName}.{name}.");
                if (Headers.Contains(name))
                {
                    warnings?.Add("native member omitted", $"{e.ObjectName}.{name}: the serialized export member is excluded from the native view");
                    r.Skip();
                    continue;
                }
                bool emptyArray = false;
                if (r.TokenType == Newtonsoft.Json.JsonToken.StartArray)
                {
                    if (!r.Read()) throw new EndOfStreamException($"Native serializer ended inside {e.ObjectName}.{name}.");
                    emptyArray = r.TokenType == Newtonsoft.Json.JsonToken.EndArray;
                    if (emptyArray)
                    {
                        warnings?.Add("native empty array omitted", $"{e.ObjectName}.{name}: the explicitly serialized empty array is excluded from the native view");
                        continue;
                    }
                    Open(); jw.WritePropertyName(name); jw.WriteStartArray();
                    while (r.TokenType != Newtonsoft.Json.JsonToken.EndArray)
                    {
                        jw.WriteToken(r, true);
                        if (!r.Read()) throw new EndOfStreamException($"Native serializer ended inside {e.ObjectName}.{name}.");
                    }
                    jw.WriteEndArray();
                    continue;
                }
                Open(); jw.WritePropertyName(name); jw.WriteToken(r, true);
            }
            if (jw != null) { jw.WriteEndObject(); jw.Flush(); w.WriteLine(); any = true; }
            else warnings?.Add("native export omitted", $"Export #{i + 1} {e.ObjectName}: no retained native members, so the export has no entry in this view");

            void Open()
            {
                if (jw != null) return;
                w.WriteLine($"#{i + 1} {e.GetExportClassType()?.Value?.Value} {e.ObjectName}");
                jw = new Newtonsoft.Json.JsonTextWriter(w) { Formatting = Newtonsoft.Json.Formatting.None, CloseOutput = false };
                jw.WriteStartObject();
            }
        }
        finally { File.Delete(tmp); }
    }
    return any;
}

}
catch (Exception ex)
{
    coverageSummary.Warnings.Add("export invocation failed", ex.GetType().Name + ": " + ex.Message.Split('\n')[0]);
    Console.Error.WriteLine(ex);
    Environment.ExitCode = 1;
}

// Writes a payload over 64 KiB to <dir>/<SHA-1>.bin beside the native file and keeps its format, size, digest and file
// name in the JSON; a smaller payload stays inline
sealed class PayloadSidecar(string root, string dir, CoverageWarnings warnings) : Newtonsoft.Json.JsonConverter<UAssetAPI.ExportTypes.Payload>
{
    public override void WriteJson(Newtonsoft.Json.JsonWriter w, UAssetAPI.ExportTypes.Payload p, Newtonsoft.Json.JsonSerializer s)
    {
        warnings.Add("labelled payload emitted", $"{w.Path}: {p.Format} ({p.Data?.LongLength ?? 0} bytes)");
        w.WriteStartObject();
        if (s.TypeNameHandling.HasFlag(Newtonsoft.Json.TypeNameHandling.Objects))
        {
            w.WritePropertyName("$type"); w.WriteValue(typeof(UAssetAPI.ExportTypes.Payload).FullName + ", UAssetAPI");
        }
        w.WritePropertyName("Format"); w.WriteValue(p.Format);
        if (root == null || p.Data == null || p.Data.Length <= 65536) { w.WritePropertyName("Data"); w.WriteValue(p.Data); w.WriteEndObject(); return; }
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(p.Data)), file = Path.Combine(dir, sha + ".bin");
        string full = Path.Combine(root, file);
        bool matches = File.Exists(full) && new FileInfo(full).Length == p.Data.LongLength;
        if (matches)
        {
            using var existing = File.OpenRead(full);
            byte[] buffer = new byte[65536];
            for (int offset = 0; offset < p.Data.Length;)
            {
                int count = Math.Min(buffer.Length, p.Data.Length - offset);
                existing.ReadExactly(buffer.AsSpan(0, count));
                if (!buffer.AsSpan(0, count).SequenceEqual(p.Data.AsSpan(offset, count))) { matches = false; break; }
                offset += count;
            }
        }
        if (!matches)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(full));
            string temporary = full + ".writing-" + Guid.NewGuid().ToString("N");
            try { File.WriteAllBytes(temporary, p.Data); File.Move(temporary, full, true); }
            finally { File.Delete(temporary); }
        }
        w.WritePropertyName("Size"); w.WriteValue(p.Data.LongLength);
        w.WritePropertyName("Sha1"); w.WriteValue(sha);
        w.WritePropertyName("File"); w.WriteValue(file);
        w.WriteEndObject();
    }
    public override UAssetAPI.ExportTypes.Payload ReadJson(Newtonsoft.Json.JsonReader r, Type t, UAssetAPI.ExportTypes.Payload v, bool h, Newtonsoft.Json.JsonSerializer s) => throw new NotSupportedException();
}
