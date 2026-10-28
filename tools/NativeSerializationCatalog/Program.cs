using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using EpicGames.UHT.Tokenizer;
using EpicGames.UHT.Utils;
using Microsoft.Extensions.Logging.Abstractions;

// Input records are find's %y\t%s\t%p\0. Archives are read through ZipArchive,
// and declarations through the same tokenizer used by UnrealHeaderTool.
if (args.Length < 5)
    throw new ArgumentException("Usage: NativeSerializationCatalog <Engine> <engine.cs> <projects.tsv> <source-prefix=local-prefix> <metadata.nul>...");
string engineDirectory = Path.GetFullPath(args[0]).TrimEnd('/') + "/";
string[] pathMapping = args[3].Split('=', 2);
if (pathMapping.Length != 2) throw new ArgumentException("Path mapping must be source-prefix=local-prefix");
string sourcePrefix = pathMapping[0], localPrefix = pathMapping[1];
var session = new UhtSession(NullLogger.Instance);
var modules = new Dictionary<string, string>(StringComparer.Ordinal);
var projectRoots = new HashSet<string>(StringComparer.Ordinal);
var pluginRoots = new HashSet<string>(StringComparer.Ordinal);
var definitions = new Dictionary<(string Scope, string Module, string Class), ClassDefinition>();
var structDefinitions = new HashSet<(string Scope, string Module, string Name)>();
var reflectedMirrors = new HashSet<(string Scope, string Module, string Name)>();
var structTraits = new Dictionary<(string Scope, string Module, string Name), StructSerializationTraits>();
long headers = 0, declarations = 0;

IEnumerable<string> MetadataPaths(string metadata)
{
    using var input = new StreamReader(metadata, new UTF8Encoding(false, true));
    var record = new StringBuilder();
    char[] buffer = new char[65536];
    int length;
    while ((length = input.Read(buffer)) > 0)
    {
        for (int index = 0; index < length; index++)
        {
            if (buffer[index] != '\0') { record.Append(buffer[index]); continue; }
            string value = record.ToString();
            record.Clear();
            if (!value.StartsWith("f\t", StringComparison.Ordinal)) continue;
            string path = value[(value.IndexOf('\t', 2) + 1)..];
            if (sourcePrefix.Length > 0 && path.StartsWith(sourcePrefix, StringComparison.Ordinal))
                path = localPrefix + path[sourcePrefix.Length..];
            yield return path;
        }
    }
    if (record.Length != 0) throw new FormatException("Unterminated metadata record: " + metadata);
}

IEnumerable<string> SourcePaths() => args.Skip(4).SelectMany(MetadataPaths);
bool IsHeader(string path) => path.EndsWith(".h", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".hpp", StringComparison.OrdinalIgnoreCase);
bool IsCppSource(string path) => IsHeader(path) || path.EndsWith(".cpp", StringComparison.OrdinalIgnoreCase)
    || path.EndsWith(".inl", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".inc", StringComparison.OrdinalIgnoreCase);

void RegisterModuleFile(string path)
{
    if (path.EndsWith(".Build.cs", StringComparison.OrdinalIgnoreCase))
        modules[Path.GetDirectoryName(path)] = Path.GetFileName(path)[..^9];
    if (path.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) projectRoots.Add(Path.GetDirectoryName(path));
    if (path.EndsWith(".uplugin", StringComparison.OrdinalIgnoreCase)) pluginRoots.Add(Path.GetDirectoryName(path));
}

string FindScope(string path)
{
    if (path.StartsWith(engineDirectory, StringComparison.Ordinal)) return "Engine";
    string plugin = null;
    for (string directory = Path.GetDirectoryName(path); directory != null; directory = Path.GetDirectoryName(directory))
    {
        if (projectRoots.Contains(directory)) return directory;
        if (plugin == null && pluginRoots.Contains(directory)) plugin = directory;
    }
    return plugin ?? Path.GetDirectoryName(path);
}

string FindModule(string path, string source, bool required = true)
{
    for (string directory = Path.GetDirectoryName(path); directory != null; directory = Path.GetDirectoryName(directory))
        if (modules.TryGetValue(directory, out string module)) return module;
    int sourceStart = path.LastIndexOf("/Source/", StringComparison.Ordinal);
    if (sourceStart >= 0)
    {
        string module = path[(sourceStart + 8)..].Split('/')[0];
        if (source.Contains(module.ToUpperInvariant() + "_API", StringComparison.Ordinal))
        {
            modules[path[..(sourceStart + 8)] + module] = module;
            return module;
        }
    }
    if (!required) return null;
    throw new FormatException("Cannot bind reflected declaration to its module: " + path);
}

bool IsPreprocessorDefinition(string source, int position)
{
    int start = source.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
    while (start > 0)
    {
        int end = start - 2;
        while (end >= 0 && source[end] == '\r') end--;
        if (end < 0 || source[end] != '\\') break;
        start = source.LastIndexOf('\n', Math.Max(0, end - 1)) + 1;
    }
    return source.AsSpan(start, position - start).TrimStart().StartsWith("#");
}

void ParseHeader(string path, string source)
{
    headers++;
    if (!IsHeader(path) && !Regex.IsMatch(source, @"\bstruct\s+(?:\w+_API\s+)?\w*StructOpsTypeTraits\s*[:<]")) return;
    if (!source.Contains("UCLASS", StringComparison.Ordinal) && !source.Contains("UINTERFACE", StringComparison.Ordinal)
        && !source.Contains("USTRUCT", StringComparison.Ordinal) && !source.Contains("StructOpsTypeTraits", StringComparison.Ordinal)) return;
    var reader = new UhtTokenBufferReader(session, source.AsMemory());
    var tokens = new List<UhtToken>();
    try { while (!reader.IsEOF) tokens.Add(reader.GetToken()); }
    catch (Exception exception) { throw new FormatException("Tokenizing " + path, exception); }
    ParseStructSerializationTraits(path, source, tokens);
    for (int index = 0; index < tokens.Count; index++)
    {
        bool isStruct = tokens[index].IsValue("USTRUCT");
        if (tokens[index].TokenType != UhtTokenType.Identifier ||
            !(isStruct || tokens[index].IsValue("UCLASS") || tokens[index].IsValue("UINTERFACE")) ||
            IsPreprocessorDefinition(source, tokens[index].InputStartPos)) continue;
        if (index + 1 >= tokens.Count || !tokens[index + 1].IsValue('(')) continue;
        index += 2;
        int parentheses = 1;
        while (index < tokens.Count && parentheses > 0)
        {
            if (tokens[index].IsValue('(')) parentheses++;
            if (tokens[index].IsValue(')')) parentheses--;
            index++;
        }
        if (index >= tokens.Count || !(tokens[index].IsValue("class") || isStruct && tokens[index].IsValue("struct")))
            throw new FormatException("Expected reflected class declaration: " + path);
        string name = null, parent = null;
        parentheses = 0;
        for (index++; index < tokens.Count && !tokens[index].IsValue(':') && !tokens[index].IsValue('{') && !tokens[index].IsValue(';'); index++)
        {
            if (tokens[index].IsValue('(')) parentheses++;
            else if (tokens[index].IsValue(')')) parentheses--;
            else if (parentheses == 0 && tokens[index].TokenType == UhtTokenType.Identifier &&
                (isStruct ? !tokens[index].Value.ToString().EndsWith("_API", StringComparison.Ordinal) && !tokens[index].IsValue("final")
                    : Regex.IsMatch(tokens[index].Value.ToString(), "^[UA][A-Z]"))) name = tokens[index].Value.ToString();
        }
        if (index < tokens.Count && tokens[index].IsValue(':'))
            for (index++; index < tokens.Count && !tokens[index].IsValue('{') && !tokens[index].IsValue(';'); index++)
                if (parent == null && tokens[index].TokenType == UhtTokenType.Identifier &&
                    Regex.IsMatch(tokens[index].Value.ToString(), "^[UA][A-Z]")) parent = tokens[index].Value.ToString();
        if (index >= tokens.Count || !tokens[index].IsValue('{') || name == null)
            throw new FormatException("Expected reflected class body: " + path);
        bool serialize = false, serializeDefault = false;
        int braces = 1;
        for (index++; index < tokens.Count && braces > 0; index++)
        {
            if (tokens[index].IsValue('{')) { braces++; continue; }
            if (tokens[index].IsValue('}')) { braces--; continue; }
            if (braces != 1) continue;
            if (tokens[index].IsValue("DECLARE_FARCHIVE_SERIALIZER") || tokens[index].IsValue("DECLARE_FSTRUCTUREDARCHIVE_SERIALIZER")) serialize = true;
            if (index == 0 || index + 3 >= tokens.Count || !tokens[index - 1].IsValue("void") || !tokens[index + 1].IsValue('(')) continue;
            int parameter = index + 2;
            if (tokens[parameter].IsValue("class")) parameter++;
            int end = parameter;
            bool archive = false;
            if (tokens[end].IsValue("FArchive") && end + 1 < tokens.Count && tokens[++end].IsValue('&')) { end++; archive = true; }
            else if (tokens[end].IsValue("FStructuredArchive") && end + 2 < tokens.Count && tokens[++end].IsValue("::") && tokens[++end].IsValue("FRecord")) { end++; archive = true; }
            else if (tokens[end].IsValue("FStructuredArchiveRecord")) { end++; archive = true; }
            if (archive)
            {
                if (end < tokens.Count && !tokens[end].IsValue(')')) end++;
                archive = end < tokens.Count && tokens[end].IsValue(')');
            }
            if (tokens[index].IsValue("Serialize") && archive) serialize = true;
            if (tokens[index].IsValue("SerializeDefaultObject") && tokens[parameter].IsValue("UObject")) serializeDefault = true;
        }
        index--;
        string scope = FindScope(path), module = FindModule(path, source);
        if (isStruct)
        {
            structDefinitions.Add((scope, module, name));
            if (Path.GetFileName(path) == "NoExportTypes.h") reflectedMirrors.Add((scope, module, name));
            continue;
        }
        declarations++;
        var key = (scope, module, name);
        if (definitions.TryGetValue(key, out var previous))
        {
            if (previous.Parent != parent) throw new FormatException("Conflicting native parent: " + path + ": " + name);
            previous.Serialize |= serialize;
            previous.SerializeDefault |= serializeDefault;
        }
        else definitions.Add(key, new ClassDefinition(scope, module, name, parent, serialize, serializeDefault));
    }
}

void ParseStructSerializationTraits(string path, string source, List<UhtToken> tokens)
{
    string TokenText(int first, int end) => string.Concat(tokens.Skip(first).Take(end - first).Select(token => token.Value.ToString()));
    for (int index = 1; index + 1 < tokens.Count; index++)
    {
        string traitType = tokens[index].Value.ToString();
        if (!traitType.EndsWith("StructOpsTypeTraits", StringComparison.Ordinal) || !tokens[index - 1].IsValue("struct")
            || IsPreprocessorDefinition(source, tokens[index].InputStartPos)) continue;
        int end = index + 1;
        string name;
        if (traitType == "TStructOpsTypeTraits" && tokens[end].IsValue('<'))
        {
            int parameter = ++end, angles = 1;
            for (; end < tokens.Count && angles > 0; end++)
            {
                if (tokens[end].IsValue('<')) angles++;
                if (tokens[end].IsValue('>')) angles--;
            }
            name = TokenText(parameter, end - 1);
        }
        else name = "$" + traitType;
        if (end >= tokens.Count || !tokens[end].IsValue(':')) continue;
        int parentStart = ++end;
        while (end < tokens.Count && !tokens[end].IsValue('{') && !tokens[end].IsValue(';')) end++;
        if (end >= tokens.Count || !tokens[end].IsValue('{')) continue;
        string parent = TokenText(parentStart, end).Replace("public", "", StringComparison.Ordinal);
        bool? serialize = null, structured = null;
        int braces = 1;
        for (end++; end < tokens.Count && braces > 0; end++)
        {
            if (tokens[end].IsValue('{')) braces++;
            if (tokens[end].IsValue('}')) braces--;
            if (!(tokens[end].IsValue("WithSerializer") || tokens[end].IsValue("WithStructuredSerializer"))
                || end + 2 >= tokens.Count || !tokens[end + 1].IsValue('=')) continue;
            bool flag = tokens[end + 2].Value.ToString() switch
            {
                "true" or "1" => true,
                "false" or "0" => false,
                _ => throw new FormatException("Nonliteral native struct serialization trait: " + path),
            };
            if (tokens[end].IsValue("WithSerializer")) serialize = flag;
            else structured = flag;
        }
        var key = (FindScope(path), FindModule(path, source), name);
        var traits = new StructSerializationTraits(parent, serialize, structured);
        if (structTraits.TryGetValue(key, out var previous) && previous != traits)
            throw new FormatException("Conflicting native struct traits: " + path + ": " + name);
        structTraits[key] = traits;
    }
}

foreach (string path in SourcePaths()) RegisterModuleFile(path);
// Orphaned installed-plugin headers can lack their Build.cs. An exported sibling
// declaration still establishes the Source/<module> directory's module identity.
foreach (string path in SourcePaths().Where(IsHeader))
    if (path.Contains("/Source/", StringComparison.Ordinal) && FindModule(path, "", required: false) == null)
        FindModule(path, File.ReadAllText(path), required: false);
foreach (string path in SourcePaths()) if (IsCppSource(path)) ParseHeader(path, File.ReadAllText(path));
foreach (string path in SourcePaths().Where(path => path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
{
    using var input = File.OpenRead(path);
    using var archive = new ZipArchive(input, ZipArchiveMode.Read);
    foreach (var entry in archive.Entries) RegisterModuleFile(path + "!/" + entry.FullName);
    foreach (var entry in archive.Entries.Where(entry => IsCppSource(entry.FullName)))
    {
        using var content = new StreamReader(entry.Open());
        ParseHeader(path + "!/" + entry.FullName, content.ReadToEnd());
    }
}

var byName = definitions.Values.GroupBy(definition => (definition.Scope, definition.Name)).ToDictionary(group => group.Key, group => group.ToArray());
var resolved = new Dictionary<(ClassDefinition, bool), bool?>();
var active = new HashSet<(ClassDefinition, bool)>();
bool? ResolveSerialization(ClassDefinition definition, bool defaults)
{
    var key = (definition, defaults);
    if (resolved.TryGetValue(key, out var result)) return result;
    if (definition.Name == "UObject" || defaults && definition.Name == "UClass") return resolved[key] = false;
    if (defaults ? definition.SerializeDefault : definition.Serialize) return resolved[key] = true;
    if (!active.Add(key)) throw new FormatException("Native inheritance cycle: " + definition.Name);
    if (!byName.TryGetValue((definition.Scope, definition.Parent), out var parents) &&
        !byName.TryGetValue(("Engine", definition.Parent), out parents)) result = null;
    else
    {
        var results = parents.Select(parent => ResolveSerialization(parent, defaults)).Distinct().ToArray();
        result = results.Length == 1 ? results[0] : null;
    }
    active.Remove(key);
    return resolved[key] = result;
}

var capabilities = definitions.Values.Select(definition =>
{
    bool? serialize = ResolveSerialization(definition, false), defaults = ResolveSerialization(definition, true);
    if (serialize == null) throw new FormatException("Native serialization cannot be classified: " + definition.Module + "." + definition.Name);
    string name = definition.Name[1..];
    if (name.StartsWith("DEPRECATED_", StringComparison.Ordinal)) name = name[11..];
    return (definition.Scope, Module: "/Script/" + definition.Module, Name: name,
        Flags: (serialize == true ? 1 : 0) | (defaults == true ? 2 : 0) | (defaults == null ? 4 : 0));
}).ToArray();

string SimpleCppName(string name)
{
    int separator = name.LastIndexOf("::", StringComparison.Ordinal);
    return separator < 0 ? name : name[(separator + 2)..];
}
bool ResolveStructSerialization((string Scope, string Module, string Name) key, bool structured, HashSet<(string, string, string)> ancestors)
{
    if (!structTraits.TryGetValue(key, out var traits)) return false; // Unspecialized TStructOpsTypeTraits inherits the all-false base.
    bool? own = structured ? traits.Structured : traits.Serialize;
    if (own.HasValue) return own.Value;
    if (!ancestors.Add(key)) throw new FormatException("Struct trait inheritance cycle: " + key);
    try
    {
        string parent = traits.Parent;
        int argumentStart = parent.IndexOf('<');
        string parentType = argumentStart < 0 ? parent : parent[..argumentStart];
        if (parentType is "TStructOpsTypeTraitsBase" or "TStructOpsTypeTraitsBase2") return false;
        string parentName = parentType == "TStructOpsTypeTraits" ? parent[(argumentStart + 1)..^1] : "$" + SimpleCppName(parentType);
        var candidates = structTraits.Keys.Where(candidate => candidate.Scope == key.Scope && candidate.Name == parentName).ToArray();
        if (candidates.Length == 0) candidates = structTraits.Keys.Where(candidate => candidate.Scope == "Engine" && candidate.Name == parentName).ToArray();
        if (candidates.Length == 0) throw new FormatException("Unclassified struct traits parent: " + key + ": " + parent);
        bool[] results = candidates.Select(candidate => ResolveStructSerialization(candidate, structured, ancestors)).Distinct().ToArray();
        if (results.Length != 1) throw new FormatException("Ambiguous struct traits parent: " + key + ": " + parent);
        return results[0];
    }
    finally { ancestors.Remove(key); }
}
var nativeStructs = structTraits.Keys.Where(key => !key.Name.StartsWith('$') &&
    (ResolveStructSerialization(key, false, new()) || ResolveStructSerialization(key, true, new())))
    .Select(key => (key.Scope, Module: "/Script/" + key.Module, Name: SimpleCppName(key.Name)[1..])).Distinct().ToArray();
var structCapabilities = structDefinitions.Select(definition =>
{
    string name = definition.Name[1..], module = "/Script/" + definition.Module;
    bool native = nativeStructs.Any(value => value.Scope == definition.Scope && value.Module == module && value.Name == name);
    // A noexport mirror does not define its C++ type; absent an explicit traits
    // specialization, this source pass has not inspected that type's aliases.
    int flags = native ? 1 : reflectedMirrors.Contains(definition) && !structTraits.ContainsKey(definition) ? 2 : 0;
    return (definition.Scope, Module: module, Name: name, Flags: flags);
}).ToArray();
foreach (var group in structCapabilities.GroupBy(value => (value.Module, value.Name)))
    if (group.Select(value => value.Flags).Distinct().Count() != 1)
        throw new FormatException("Project struct serialization capabilities disagree: " + group.Key);
foreach (var group in capabilities.GroupBy(value => (value.Module, value.Name)))
    if (group.Select(value => value.Flags).Distinct().Count() != 1)
        throw new FormatException("Project serializer capabilities disagree: " + group.Key);
using (var output = new StreamWriter(args[1]))
{
    output.WriteLine("// Generated by tools/NativeSerializationCatalog. Do not edit.");
    output.WriteLine("using System.Collections.Generic;\nnamespace UAssetAPI.ExportTypes;\ninternal static class NativeSerializationTypes\n{\n    internal static readonly Dictionary<(string Module, string Class), NativeSerializationKind> Classes = new()\n    {");
    foreach (var value in capabilities.Where(value => value.Scope == "Engine").OrderBy(value => value.Module, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal))
        output.WriteLine($"        [(\"{value.Module}\", \"{value.Name}\")] = (NativeSerializationKind){value.Flags},");
    output.WriteLine("    };\n    internal static readonly Dictionary<(string Module, string Name), byte> Structs = new()\n    {");
    foreach (var value in structCapabilities.Where(value => value.Scope == "Engine").OrderBy(value => value.Module, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal))
        output.WriteLine($"        [(\"{value.Module}\", \"{value.Name}\")] = {value.Flags},");
    output.WriteLine("    };\n}");
}
using (var output = new StreamWriter(args[2]))
    foreach (var value in capabilities.Where(value => value.Scope != "Engine").Select(value => (value.Module, value.Name, value.Flags)).Distinct().OrderBy(value => value.Module, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal))
        output.WriteLine($"{value.Module}\t{value.Name}\t{value.Flags}");
using (var output = new StreamWriter(Path.Combine(Path.GetDirectoryName(args[2]), "project-struct-serializers.tsv")))
    foreach (var value in structCapabilities.Where(value => value.Scope != "Engine").Select(value => (value.Module, value.Name, value.Flags)).Distinct().OrderBy(value => value.Module, StringComparer.Ordinal).ThenBy(value => value.Name, StringComparer.Ordinal))
        output.WriteLine($"{value.Module}\t{value.Name}\t{value.Flags}");
Console.WriteLine($"Source files {headers}; class declarations {declarations}; class definitions {definitions.Count}; native known {capabilities.Length}/{definitions.Count}; default known {capabilities.Count(value => (value.Flags & 4) == 0)}/{definitions.Count}; struct declarations {structDefinitions.Count}; traits {structTraits.Count}; native structs {nativeStructs.Length}");

sealed class ClassDefinition(string scope, string module, string name, string parent, bool serialize, bool serializeDefault)
{
    public readonly string Scope = scope, Module = module, Name = name, Parent = parent;
    public bool Serialize = serialize, SerializeDefault = serializeDefault;
}

sealed record StructSerializationTraits(string Parent, bool? Serialize, bool? Structured);
