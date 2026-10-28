using System;
using System.Collections.Generic;
using System.Linq;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace UAssetAPI.PropertyTypes.Structs;

/// <summary>Source-declared native struct serializers, including types without a specialized property reader.</summary>
public static class NativeStructSerialization
{
    static readonly Dictionary<(string Module, string Name), byte> ProjectStructs = new();

    /// <summary>Register source-derived project struct serialization capabilities.</summary>
    public static void RegisterProjectSerialization(string module, string name, bool native, bool inspected = true)
    {
        ProjectStructs[(module, name)] = (byte)(inspected ? native ? 1 : 0 : 2);
    }

    internal static string ResolveModule(UAsset asset, FName structType, FPropertyTypeName typeName)
    {
        string type = structType?.ToString();
        if (type == null) return null;
        string module = typeName?.GetParameter(0).GetParameter(0).GetName()?.ToString();
        int qualifiedSeparator = type.LastIndexOf('.');
        if (type.StartsWith("/", StringComparison.Ordinal) && qualifiedSeparator > 0)
            return type[..qualifiedSeparator];
        if (module?.StartsWith("/", StringComparison.Ordinal) == true) return module;
        string[] imports = asset.Imports?.Where(import => import.ObjectName?.ToString() == type
            && (import.ClassName?.ToString() is "ScriptStruct" or "UserDefinedStruct") && import.OuterIndex?.IsImport() == true)
            .Select(import => import.OuterIndex.ToImport(asset).ObjectName?.ToString())
            .Where(path => path != null).Distinct(StringComparer.Ordinal).ToArray() ?? Array.Empty<string>();
        return imports.Length == 1 ? imports[0] : null;
    }

    internal static void ReportEncounter(UAsset asset, FName name, FName structType, FPropertyTypeName typeName, UsmapSchema schema)
    {
        string type = structType?.ToString();
        if (type == null) return;
        string module = ResolveModule(asset, structType, typeName) ?? schema?.ModulePath;
        int qualifiedSeparator = type.LastIndexOf('.');
        if (type.StartsWith("/", StringComparison.Ordinal) && qualifiedSeparator > 0)
        {
            type = type[(qualifiedSeparator + 1)..];
        }
        if (module?.StartsWith("/Game/", StringComparison.Ordinal) == true) return; // An asset-defined struct has no native C++ operations.
        byte capabilities = 2;
        bool classified = module != null && (ProjectStructs.TryGetValue((module, type), out capabilities)
            || NativeSerializationTypes.Structs.TryGetValue((module, type), out capabilities));
        if (!classified || capabilities == 2)
            asset.Coverage.Report("uninspected-struct-serialization", $"{name}: native serialization declaration inspection stopped for {module ?? "<unqualified>"}.{type}; this check did not resolve its qualified C++ capabilities.");
        else if (capabilities == 1)
            asset.Coverage.Report("custom-serialization", $"{name}: encountered {module}.{type}, whose source declares native struct serialization, including zero-body representations.");
    }
}
