using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

// A readable dump for reviewing: every export with its class and outer, its tagged properties, and for graph nodes
// each pin's type, default and the node.pin it links to.
sealed class TextDump(CoverageWarnings warnings)
{
    public static void Write(UAsset a, TextWriter o, CoverageWarnings warnings) => new TextDump(warnings).Write(a, o);

    void Write(UAsset a, TextWriter o)
    {
        warnings.Add("text package summary omitted", $"PackageFlags={a.PackageFlags}: the package summary flags are not printed by the text formatter");
        // a link names its owning node, then the pin by id within that node
        string Link(EdGraphPinReference l)
        {
            var node = l.OwningNode != null && l.OwningNode.IsExport() ? l.OwningNode.ToExport(a) as EdGraphNodeExport : null;
            var pin = node?.Pins?.FirstOrDefault(p => p.PinId == l.PinId);
            if (pin == null) warnings.Add("unresolved text pin", $"{l.OwningNode?.Index}/{l.PinId}: the text formatter could not name the connected pin");
            return $"{node?.ObjectName.ToString() ?? Ref(a, l.OwningNode)}.{pin?.PinName.ToString() ?? "?"}";
        }
        for (int i = 0; i < a.Exports.Count; i++)
        {
            var e = a.Exports[i];
            ReportOmittedNativeMembers(e);
            o.WriteLine($"#{i + 1} {e.GetExportClassType()?.Value?.Value} {e.ObjectName}  outer={Ref(a, e.OuterIndex)}");
            if (e is NormalExport n)
            {
                foreach (var p in n.Data) Prop(a, p, o, "  ");
                // UBlueprintGeneratedClass::SerializeDefaultObject's sparse class data, after the default object's properties
                if (n.SparseClassDataStruct != null && !n.SparseClassDataStruct.IsNull())
                {
                    o.WriteLine($"  SparseClassData ({Ref(a, n.SparseClassDataStruct)}):");
                    if (n.SparseClassData != null) foreach (var p in n.SparseClassData) Prop(a, p, o, "    ");
                }
            }
            if (e is RigVMLinkExport rl) o.WriteLine($"  link {rl.SourcePinPath} -> {rl.TargetPinPath}");
            if (e is EdGraphNodeExport g && g.Pins != null)
                foreach (var p in g.Pins)
                {
                    warnings.Add("text pin members omitted", $"{e.ObjectName}.{p.PinName}: pin identity {p.PinId} and native pin metadata are not printed by the text formatter");
                    var t = p.PinType;
                    var type = $"{t.PinCategory}{(t.PinSubCategoryObject?.Index != 0 ? ":" + Ref(a, t.PinSubCategoryObject) : t.PinSubCategory?.Value?.Value is { Length: > 0 } s ? ":" + s : "")}{(t.ContainerType != EPinContainerType.None ? "[" + t.ContainerType + "]" : "")}";
                    var sb = new StringBuilder($"  pin {(p.Direction == EEdGraphPinDirection.Input ? "in " : "out")} {p.PinName} : {type}");
                    if (p.DefaultValue?.Value is { Length: > 0 } dv) sb.Append($" = {dv}");
                    if (p.DefaultObject?.Index != 0 && p.DefaultObject != null) sb.Append($" = {Ref(a, p.DefaultObject)}");
                    if (p.DefaultTextValue?.Value?.Value is { Length: > 0 } tv) sb.Append($" = \"{tv}\"");
                    if (p.LinkedTo?.Length > 0) sb.Append(" -> " + string.Join(", ", p.LinkedTo.Select(Link)));
                    if (p.ParentPin != null && p.ParentPin.OwningNode?.Index != 0) sb.Append(" (split)");
                    o.WriteLine(sb);
                }
        }
    }

    // The readable view prints tagged values and selected graph fields, not every decoded native member.
    // Record the members actually present on this export; empty buffers/collections carry no omitted content.
    void ReportOmittedNativeMembers(Export export)
    {
        var contract = UAsset.CreateReadableJsonSerializer().ContractResolver.ResolveContract(export.GetType()) as Newtonsoft.Json.Serialization.JsonObjectContract;
        foreach (var member in contract?.Properties.AsEnumerable() ?? Enumerable.Empty<Newtonsoft.Json.Serialization.JsonProperty>())
        {
            if (member.Ignored || !member.Readable) continue;
            if (member.PropertyName is "ObjectName" or "ClassIndex" or "OuterIndex") continue;
            if (member.PropertyName is "Data" && export is NormalExport) continue;
            if (member.PropertyName is "SparseClassDataStruct" or "SparseClassData") continue;
            if (export is RigVMLinkExport && member.PropertyName is "SourcePinPath" or "TargetPinPath") continue;
            if (export is EdGraphNodeExport && member.PropertyName == "Pins") continue;
            if (member.ShouldSerialize != null && !member.ShouldSerialize(export)) continue;
            var value = member.ValueProvider.GetValue(export);
            if (value == null || value is System.Collections.ICollection { Count: 0 }) continue;
            warnings.Add("text native member omitted", $"{export.ObjectName}.{member.PropertyName}: decoded {member.PropertyType.Name} is not printed by the text formatter");
        }
    }

    string Ref(UAsset a, FPackageIndex i)
    {
        if (i == null || i.Index == 0) return "None";
        try
        {
            if (i.IsImport()) { var im = i.ToImport(a); return im.OuterIndex.Index != 0 && !im.OuterIndex.ToImport(a).OuterIndex.IsNull() ? $"{Ref(a, im.OuterIndex)}.{im.ObjectName}" : $"{im.OuterIndex.ToImport(a).ObjectName}.{im.ObjectName}"; }
            var ex = i.ToExport(a); return $"{ex.ObjectName}#{i.Index}";
        }
        catch (Exception ex)
        {
            warnings.Add("unresolved text reference", $"Package index {i.Index}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return $"?{i.Index}";
        }
    }

    void Prop(UAsset a, PropertyData p, TextWriter o, string ind)
    {
        var name = NameText(p.Name) + (p.ArrayIndex > 0 ? $"[{p.ArrayIndex}]" : "");
        switch (p)
        {
            case InstancedStructPropertyData ins:
                o.WriteLine($"{ind}{name} (instanced {Ref(a, ins.Struct)}):");
                foreach (var c in ins.Value?.Value ?? new()) Prop(a, c, o, ind + "  ");
                break;
            case InstancedStructContainerPropertyData isc:
                o.WriteLine($"{ind}{name} [{isc.Value?.Count ?? 0}]:");
                for (int k = 0; k < (isc.Value?.Count ?? 0); k++)
                {
                    o.WriteLine($"{ind}  [{k}] ({Ref(a, isc.Value[k].Struct)}){(isc.Value[k].SkippedBytes?.Length > 0 ? $" {isc.Value[k].SkippedBytes.Length} undecoded bytes" : "")}");
                    foreach (var c in isc.Value[k].Value?.Value ?? new()) Prop(a, c, o, ind + "    ");
                }
                break;
            case StructPropertyData s when s.Value != null && s.Value.Count > 0:
                o.WriteLine($"{ind}{name} ({NameText(s.StructType)}):");
                foreach (var c in s.Value) Prop(a, c, o, ind + "  ");
                break;
            case ArrayPropertyData arr when arr.Value != null:
                if (arr.Value.All(v => v is not StructPropertyData && v is not ArrayPropertyData && v is not MapPropertyData))
                    o.WriteLine($"{ind}{name} = [{string.Join(", ", arr.Value.Select(v => Val(a, v)))}]");
                else
                {
                    o.WriteLine($"{ind}{name} [{arr.Value.Length}]:");
                    for (int k = 0; k < arr.Value.Length; k++)
                    {
                        if (arr.Value[k] is StructPropertyData es) { o.WriteLine($"{ind}  [{k}] ({NameText(es.StructType)})"); foreach (var c in es.Value ?? new()) Prop(a, c, o, ind + "    "); }
                        else Prop(a, arr.Value[k], o, ind + $"  [{k}] ");
                    }
                }
                break;
            case MapPropertyData m when m.Value != null:
                o.WriteLine($"{ind}{name} {{{m.Value.Count}}}:");
                foreach (var kv in m.Value)
                {
                    if (kv.Value is StructPropertyData vs) { o.WriteLine($"{ind}  {Val(a, kv.Key)} =>"); foreach (var c in vs.Value ?? new()) Prop(a, c, o, ind + "    "); }
                    else o.WriteLine($"{ind}  {Val(a, kv.Key)} => {Val(a, kv.Value)}");
                }
                break;
            case PropertyData bag when bag.RawValue is List<PropertyData> inner && inner.Count > 0:
                o.WriteLine($"{ind}{name} ({p.GetType().Name.Replace("PropertyData", "")}):");
                foreach (var c in inner) Prop(a, c, o, ind + "  ");
                break;
            default:
                o.WriteLine($"{ind}{name} = {Val(a, p)}");
                break;
        }
    }

    string Val(UAsset a, PropertyData p)
    {
        switch (p)
        {
            case ObjectPropertyData op: return Ref(a, op.Value);
            case SoftObjectPropertyData so: return $"{NameText(so.Value.AssetPath.PackageName)}.{NameText(so.Value.AssetPath.AssetName)}{(so.Value.SubPathString?.Value is { Length: > 0 } sp ? ":" + sp : "")}";
            case NamePropertyData np: return NameText(np.Value) ?? "None";
            case StrPropertyData sp2: return $"\"{sp2.Value?.Value}\"";
            case TextPropertyData tp:
                var omitted = new List<string>();
                if (tp.Flags != 0) omitted.Add("Flags=" + tp.Flags);
                if (tp.Namespace?.Value?.Length > 0) omitted.Add("Namespace");
                if (tp.DevNotes?.Value?.Length > 0) omitted.Add("DevNotes");
                if (tp.HistoryType == TextHistoryType.Base && tp.Value?.Value?.Length > 0) omitted.Add("localization binding (Base history)");
                if (tp.HistoryType is not (TextHistoryType.Base or TextHistoryType.None or TextHistoryType.RawText)) omitted.Add("history fields (" + tp.HistoryType + ")");
                if (omitted.Count > 0) warnings.Add("text omission", $"{tp.Name}: string preview omits {string.Join(", ", omitted)}");
                return $"\"{tp.CultureInvariantString?.Value ?? tp.Value?.Value}\"";
            case EnumPropertyData ep: return NameText(ep.Value) ?? "None";
            case BytePropertyData bp: return bp.ByteType == BytePropertyType.FName ? NameText(bp.EnumValue) ?? "None" : bp.Value.ToString();
            case StructPropertyData s: return "{" + string.Join(", ", (s.Value ?? new()).Select(c => $"{InlineName(c)}={Val(a, c)}")) + "}";
            case ArrayPropertyData arr: return "[" + string.Join(", ", (arr.Value ?? []).Select(v => Val(a, v))) + "]";
        }
        if (p is NativeFieldsPropertyData native && native.Fields?.Count > 0)
        {
            warnings.Add("text omission", $"{p.Name}: native struct fields are not printed by the text formatter");
        }
        var raw = p.RawValue;
        if (raw == null)
        {
            if (p is not NativeFieldsPropertyData)
            {
                var contract = UAsset.CreateReadableJsonSerializer().ContractResolver.ResolveContract(p.GetType()) as Newtonsoft.Json.Serialization.JsonObjectContract;
                foreach (var member in contract?.Properties.AsEnumerable() ?? Enumerable.Empty<Newtonsoft.Json.Serialization.JsonProperty>())
                {
                    if (member.Ignored || !member.Readable || member.DeclaringType == typeof(PropertyData) || member.ShouldSerialize != null && !member.ShouldSerialize(p)) continue;
                    var value = member.ValueProvider.GetValue(p);
                    if (value == null || value is System.Collections.ICollection { Count: 0 }) continue;
                    warnings.Add("text omission", $"{p.Name}.{member.PropertyName}: {p.GetType().Name} has no RawValue; this serialized member is not printed");
                }
            }
            return "null";
        }
        if (raw is string || raw.GetType().IsPrimitive) return raw.ToString();
        // small math values (vectors, rotators, quaternions, colours): their numeric fields in order
        var fields = raw.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (fields.Length is > 0 and <= 6 && fields.All(f => f.FieldType.IsPrimitive || f.FieldType.Name == "FFloat16"))
            return "(" + string.Join(", ", fields.Select(f => $"{f.Name}={Convert.ToString(f.GetValue(raw), System.Globalization.CultureInfo.InvariantCulture)}")) + ")";
        var props = raw.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Where(q => q.GetIndexParameters().Length == 0 && (q.PropertyType == typeof(double) || q.PropertyType == typeof(float))).ToArray();
        if (props.Any(q => q.PropertyType == typeof(double))) props = props.Where(q => q.PropertyType == typeof(double)).ToArray();
        if (props.Length is > 0 and <= 4 && fields.Length == 0)
            return "(" + string.Join(", ", props.Select(q => $"{q.Name}={Convert.ToString(q.GetValue(raw), System.Globalization.CultureInfo.InvariantCulture)}")) + ")";
        try
        {
            var serializer = UAsset.CreateJsonSerializer();
            serializer.ContractResolver = new PayloadWarningResolver(serializer.ContractResolver, warnings);
            serializer.Converters.Insert(0, new PayloadSidecar(null, null, warnings));
            using var preview = new JsonPreviewWriter();
            serializer.Serialize(new Newtonsoft.Json.JsonTextWriter(preview) { CloseOutput = false }, raw);
            if (preview.Count <= 400) return preview.ToString();
            warnings.Add("text truncation", $"{p.Name}: serialized value has {preview.Count} characters; only 400 are printed");
            return preview + "…";
        }
        catch (Exception ex)
        {
            warnings.Add("text serialization failed", $"{p.Name}: {ex.GetType().Name}: {ex.Message.Split('\n')[0]}");
            return raw.ToString();
        }
    }

    string NameText(FName name)
    {
        if (name?.Number > 0) warnings.Add("text name suffix omitted", $"{name}: numeric name suffix {name.Number - 1} is not printed");
        return name?.Value?.Value;
    }

    string InlineName(PropertyData property)
    {
        if (property.ArrayIndex > 0) warnings.Add("text array index omitted", $"{property.Name}: static array index {property.ArrayIndex} is not printed in the inline struct");
        return NameText(property.Name);
    }

    sealed class JsonPreviewWriter : TextWriter
    {
        readonly StringBuilder prefix = new(400);
        public long Count { get; private set; }
        public override Encoding Encoding => Encoding.UTF8;
        public override void Write(char value) { if (Count++ < 400) prefix.Append(value); }
        public override void Write(char[] value, int offset, int count)
        {
            int kept = (int)Math.Min(count, Math.Max(0, 400 - Count));
            if (kept > 0) prefix.Append(value, offset, kept);
            Count += count;
        }
        public override void Write(string value)
        {
            if (value == null) return;
            int kept = (int)Math.Min(value.Length, Math.Max(0, 400 - Count));
            if (kept > 0) prefix.Append(value, 0, kept);
            Count += value.Length;
        }
        public override string ToString() => prefix.ToString();
    }
}
