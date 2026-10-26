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
static class TextDump
{
    public static void Write(UAsset a, TextWriter o)
    {
        // a link names its owning node, then the pin by id within that node
        string Link(EdGraphPinReference l)
        {
            var node = l.OwningNode != null && l.OwningNode.IsExport() ? l.OwningNode.ToExport(a) as EdGraphNodeExport : null;
            var pin = node?.Pins?.FirstOrDefault(p => p.PinId == l.PinId);
            return $"{node?.ObjectName.ToString() ?? Ref(a, l.OwningNode)}.{pin?.PinName.ToString() ?? "?"}";
        }
        for (int i = 0; i < a.Exports.Count; i++)
        {
            var e = a.Exports[i];
            o.WriteLine($"#{i + 1} {e.GetExportClassType()?.Value?.Value} {e.ObjectName}  outer={Ref(a, e.OuterIndex)}");
            if (e is NormalExport n)
                foreach (var p in n.Data) Prop(a, p, o, "  ");
            if (e is EdGraphNodeExport g && g.Pins != null)
                foreach (var p in g.Pins)
                {
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

    static string Ref(UAsset a, FPackageIndex i)
    {
        if (i == null || i.Index == 0) return "None";
        try
        {
            if (i.IsImport()) { var im = i.ToImport(a); return im.OuterIndex.Index != 0 && !im.OuterIndex.ToImport(a).OuterIndex.IsNull() ? $"{Ref(a, im.OuterIndex)}.{im.ObjectName}" : $"{im.OuterIndex.ToImport(a).ObjectName}.{im.ObjectName}"; }
            var ex = i.ToExport(a); return $"{ex.ObjectName}#{i.Index}";
        }
        catch { return $"?{i.Index}"; }
    }

    static void Prop(UAsset a, PropertyData p, TextWriter o, string ind)
    {
        var name = p.Name?.Value?.Value + (p.ArrayIndex > 0 ? $"[{p.ArrayIndex}]" : "");
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
                o.WriteLine($"{ind}{name} ({s.StructType?.Value?.Value}):");
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
                        if (arr.Value[k] is StructPropertyData es) { o.WriteLine($"{ind}  [{k}] ({es.StructType?.Value?.Value})"); foreach (var c in es.Value ?? new()) Prop(a, c, o, ind + "    "); }
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
            default:
                o.WriteLine($"{ind}{name} = {Val(a, p)}");
                break;
        }
    }

    static string Val(UAsset a, PropertyData p)
    {
        switch (p)
        {
            case ObjectPropertyData op: return Ref(a, op.Value);
            case SoftObjectPropertyData so: return $"{so.Value.AssetPath.PackageName?.Value?.Value}.{so.Value.AssetPath.AssetName?.Value?.Value}{(so.Value.SubPathString?.Value is { Length: > 0 } sp ? ":" + sp : "")}";
            case NamePropertyData np: return np.Value?.Value?.Value ?? "None";
            case StrPropertyData sp2: return $"\"{sp2.Value?.Value}\"";
            case TextPropertyData tp: return $"\"{tp.CultureInvariantString?.Value ?? tp.Value?.Value}\"";
            case EnumPropertyData ep: return ep.Value?.Value?.Value ?? "None";
            case BytePropertyData bp: return bp.ByteType == BytePropertyType.FName ? bp.EnumValue?.Value?.Value ?? "None" : bp.Value.ToString();
            case StructPropertyData s: return "{" + string.Join(", ", (s.Value ?? new()).Select(c => $"{c.Name?.Value?.Value}={Val(a, c)}")) + "}";
            case ArrayPropertyData arr: return "[" + string.Join(", ", (arr.Value ?? []).Select(v => Val(a, v))) + "]";
        }
        var raw = p.RawValue;
        if (raw == null) return "null";
        if (raw is string || raw.GetType().IsPrimitive) return raw.ToString();
        // small math values (vectors, rotators, quaternions, colours): their numeric fields in order
        var fields = raw.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (fields.Length is > 0 and <= 6 && fields.All(f => f.FieldType.IsPrimitive || f.FieldType.Name == "FFloat16"))
            return "(" + string.Join(", ", fields.Select(f => $"{f.Name}={Convert.ToString(f.GetValue(raw), System.Globalization.CultureInfo.InvariantCulture)}")) + ")";
        var props = raw.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance).Where(q => q.GetIndexParameters().Length == 0 && (q.PropertyType == typeof(double) || q.PropertyType == typeof(float))).ToArray();
        if (props.Any(q => q.PropertyType == typeof(double))) props = props.Where(q => q.PropertyType == typeof(double)).ToArray();
        if (props.Length is > 0 and <= 4 && fields.Length == 0)
            return "(" + string.Join(", ", props.Select(q => $"{q.Name}={Convert.ToString(q.GetValue(raw), System.Globalization.CultureInfo.InvariantCulture)}")) + ")";
        try { var j = a.SerializeJsonObject(raw); return j.Length > 400 ? j[..400] + "…" : j; } catch { return raw.ToString(); }
    }
}
