using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Serialization;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

// Walk the serializer's actual contract, including native fields, sparse data, rigs and bytecode.
// Limits constrain inspection, not output; hitting one is itself an explicit coverage diagnostic.
sealed class CoverageInspector(string package, TextWriter records, bool firstFinding = false)
{
    const int MaximumDepth = 12;
    internal const int MaximumCollectionItems = 4097;
    readonly IContractResolver resolver = UAsset.CreateReadableJsonSerializer().ContractResolver;
    readonly HashSet<object> ancestors = new(ReferenceEqualityComparer.Instance);
    bool found;

    void Report(string kind, string path, string detail, long bytes = 0)
    {
        records.WriteLine($"{kind}\t-\t{bytes}\t{path}: {detail.Replace('\n', ' ').Replace('\r', ' ')}\t{package}");
        found = true;
    }

    public void Inspect(UAsset asset)
    {
        foreach (var diagnostic in asset.Coverage.Diagnostics)
        {
            Report(diagnostic.Kind, package, $"occurrences: {diagnostic.Count}; example: {diagnostic.Detail}");
            if (firstFinding) return;
        }
        // Package-level fields are inspected too: thumbnails, bulk/trailer buffers and data-resource references.
        InspectMembers(asset, "Package", 0, skipExports: true);
        foreach (var export in asset.Exports)
        {
            if (firstFinding && found) break;
            if (export.Extras?.Length > 0 && export.NativeTailFailure != null)
                Report("native-parse-failed", export.ObjectName?.ToString() ?? "Export", export.NativeTailFailure, export.Extras.LongLength);
            Visit(export, export.ObjectName?.ToString() ?? "Export", 0);
        }
    }

    void Visit(object value, string path, int depth)
    {
        if (firstFinding && found) return;
        if (value == null) return;
        if (value is float f && float.IsNaN(f) || value is double d && double.IsNaN(d))
            Report("numeric-output-loss", path, "JSON represents this NaN as a string without its IEEE sign, signalling bit or payload bits");
        if (value is PropertyData { HasCustomStructSerialization: true } custom)
            Report("custom-serialization", path, $"encountered the custom binary property reader {custom.GetType().Name} for {custom.PropertyType}");
        if (value is FObjectDataResource { SerialSize: > 0 } resource)
            Report("unloaded-data-resource", path, $"data resource references {resource.SerialSize} stored bytes at offset {resource.SerialOffset}; the resource table does not load or decode its payload", resource.SerialSize);
        if (value is UnknownPropertyData unknown) { Report("rawprop", path, unknown.SerializingPropertyType?.ToString() ?? "unknown property", unknown.Value?.Length ?? 0); return; }
        if (value is RawStructPropertyData rawStruct) { Report("rawstruct", path, rawStruct.StructType + ": " + rawStruct.FailureReason, rawStruct.Value?.Length ?? 0); return; }
        if (value is Payload payload) { Report("payload", path, payload.Format, payload.Data?.Length ?? 0); return; }
        if (value is byte[] bytes)
        {
            if (bytes.Length > 0 && !(bytes.Length == 2 && path.EndsWith("StripFlags", StringComparison.Ordinal)))
                Report("blob", path, "bytes retained without a decoded representation or payload format", bytes.Length);
            return;
        }
        var type = value.GetType();
        if (type.IsPrimitive || type.IsEnum || value is string or decimal or Guid or DateTime or DateTimeOffset or TimeSpan or FName or FString or FPackageIndex) return;
        if (resolver.ResolveContract(type) is JsonPrimitiveContract or JsonStringContract) return;
        if (depth > MaximumDepth) { Report("pruned-depth", path, $"inspection stopped at depth {depth}; limit {MaximumDepth}"); return; }
        if (value is RigHierarchyExport hierarchy && !hierarchy.FullyParsed) Report("rigparse", path, hierarchy.ParseFailure);
        if (value is RigVMExport vm && !vm.FullyParsed) Report("rigparse", path, vm.ParseFailure);
        if (value is RawExport raw && value is not RigHierarchyExport && value is not RigVMExport)
        {
            if ((raw.Data?.Length ?? 0) > 0 || raw.SerialSize > 0) Report("raw", path, "export is opaque", (long?)raw.Data?.Length ?? raw.SerialSize);
            return;
        }
        if (!type.IsValueType && !ancestors.Add(value)) { Report("output-cycle", path, "serializer omits this reference cycle"); return; }
        try
        {
            if (value is IDictionary dictionary)
            {
                if (value is IDictionary<string, object> fields)
                {
                    if (fields.TryGetValue("SizeOnDisk", out var size) && Convert.ToInt64(size) > 0 && fields.TryGetValue("Payload", out var body) && body == null)
                        Report("unloaded-payload", path, $"bulk payload of {size} bytes is referenced but not loaded");
                    if (fields.TryGetValue("PayloadSize", out size) && Convert.ToInt64(size) > 0 && !fields.ContainsKey("Payload"))
                        Report("unloaded-payload", path, $"editor bulk payload of {size} bytes is referenced but not loaded");
                }
                int count = 0;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (count++ == MaximumCollectionItems) { Report("pruned-collection", path, $"more than {MaximumCollectionItems} dictionary entries; inspection stopped"); break; }
                    Visit(entry.Key, path + ".Key", depth + 1);
                    Visit(entry.Value, path + "." + entry.Key, depth + 1);
                }
            }
            else if (value is IEnumerable sequence)
            {
                int count = 0;
                foreach (var entry in sequence)
                {
                    if (count == MaximumCollectionItems) { Report("pruned-collection", path, $"more than {MaximumCollectionItems} entries; inspection stopped"); break; }
                    Visit(entry, path + $"[{count++}]", depth + 1);
                }
            }
            else InspectMembers(value, path, depth);
        }
        finally { if (!type.IsValueType) ancestors.Remove(value); }
    }

    void InspectMembers(object value, string path, int depth, bool skipExports = false)
    {
        if (resolver.ResolveContract(value.GetType()) is not JsonObjectContract contract)
        {
            Report("uninspected-type", path, value.GetType().FullName);
            return;
        }
        foreach (var property in contract.Properties)
        {
            if (firstFinding && found) break;
            if (property.Ignored || !property.Readable || (skipExports && property.PropertyName == "Exports")) continue;
            // Fully parsed rigs retain their original bytes for write-back; they are not another undecoded payload.
            if (property.PropertyName == "Data" && (value is RigHierarchyExport { FullyParsed: true } || value is RigVMExport { FullyParsed: true })) continue;
            if (property.PropertyName == "LeadingNative" && value is NormalExport { LeadingFields: not null }) continue;
            if (property.PropertyName == "Bytes" && value is NativeFieldsPropertyData) continue;
            // A hash is already represented by its bytes; padding has no serialized value to decode.
            if (property.PropertyName == "SavedHash" && value is UAsset) continue;
            if (property.PropertyName == "padding" && (value is FMovieSceneTangentData || value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(FMovieSceneValue<>))) continue;
            object member;
            try
            {
                if (property.ShouldSerialize != null && !property.ShouldSerialize(value)) continue;
                member = property.ValueProvider.GetValue(value);
            }
            catch (Exception ex) { Report("inspection-failed", path + "." + property.PropertyName, ex.Message); continue; }
            if (member == null) continue;
            if (property.PropertyName == "AdditionalFiles" && value is UAsset && member is byte[] { Length: 4 } footer
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(footer) == UAsset.UASSET_MAGIC) continue;
            if (property.AttributeProvider.GetAttributes(typeof(UAssetAPI.JSON.PayloadFormatAttribute), true).FirstOrDefault() is UAssetAPI.JSON.PayloadFormatAttribute format && member is byte[] payload)
            {
                Report("payload", path + "." + property.PropertyName, format.Format, payload.Length);
            }
            else Visit(member, path + "." + property.PropertyName, depth + 1);
        }
    }
}
