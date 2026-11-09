using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

// Read saved JSON tokens, stopping at the first collection that the current audit would prune.
// TMap's converter adds two-element entry arrays; its outer array still counts the actual dictionary entries.
static class SavedCoverageInspector
{
    public static bool FindTextOmission(string path, CoverageWarnings warnings)
    {
        bool firstExport = true;
        foreach (string line in File.ReadLines(path))
        {
            var exportNumber = System.Text.RegularExpressions.Regex.Match(line, @"^#(?<index>\d+) ");
            if (firstExport && exportNumber.Success && long.Parse(exportNumber.Groups["index"].Value) > 1)
            {
                warnings.Add("selected exports", $"{path}: text begins after export #1; earlier exports are absent from this extract");
                return true;
            }
            if (exportNumber.Success) firstExport = false;
            if (line.StartsWith("  pin in  ", StringComparison.Ordinal) || line.StartsWith("  pin out ", StringComparison.Ordinal))
            {
                warnings.Add("text pin members omitted", $"{path}: {line.Trim().Split(':')[0]}: the serialized pin identity and native pin metadata are not printed by the text formatter");
                return true;
            }
            var header = System.Text.RegularExpressions.Regex.Match(line, @"^#\d+ (?<type>Function|BlueprintGeneratedClass|AnimBlueprintGeneratedClass|WidgetBlueprintGeneratedClass|Enum|UserDefinedEnum|ScriptStruct|UserDefinedStruct) (?<name>\S+)  outer=");
            if (header.Success)
            {
                string type = header.Groups["type"].Value;
                string member = type == "Function" ? "FunctionFlags" : type.EndsWith("GeneratedClass", StringComparison.Ordinal) ? "ClassFlags" : type.EndsWith("Enum", StringComparison.Ordinal) ? "Enum.CppForm" : "SuperStruct";
                warnings.Add("text native member omitted", $"{path}: {header.Groups["name"].Value}.{member}: the serialized native field is not printed by the text formatter");
                return true;
            }
            int separator = line.IndexOf(" = ", StringComparison.Ordinal);
            if (separator >= 0 && line.Length - separator - 3 == 401 && line[^1] == '…' && line[separator + 3] is '{' or '[')
            {
                warnings.Add("text truncation", $"{path}: {line[..separator].Trim()}: JSON preview was cut to 400 characters and an ellipsis");
                return true;
            }
        }
        return false;
    }

    public static bool FindNativeOmission(string path, CoverageWarnings warnings)
    {
        if (!path.EndsWith(".native.json", StringComparison.Ordinal)) return false;
        string peer = path[..^12] + ".txt";
        if (!File.Exists(peer)) return false;
        string export = "";
        foreach (string line in File.ReadLines(peer))
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^#\d+ ")) export = line;
            else if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^  [^ \t]+(?: = | \(| \[| \{)"))
            {
                warnings.Add("native tagged properties omitted", $"{path}: {export}: the native view excludes the property block {line.Trim().Split('=')[0].Trim()} printed in its companion text file");
                return true;
            }
        }
        return false;
    }

    public static bool FindCollectionPruning(string path, CoverageWarnings warnings)
    {
        using var input = new HashCommentReader(new StreamReader(path));
        using var reader = new JsonTextReader(input) { SupportMultipleContent = true, MaxDepth = null, DateParseHandling = DateParseHandling.None };
        var containers = new Stack<int>(); // -1 is an object; nonnegative values count an array's entries.
        try
        {
            while (reader.Read())
            {
                if (reader.TokenType is JsonToken.Comment or JsonToken.PropertyName) continue;
                if (reader.TokenType is JsonToken.EndArray or JsonToken.EndObject) { containers.Pop(); continue; }
                if (containers.TryPeek(out int count) && count >= 0)
                {
                    containers.Pop(); containers.Push(++count);
                    if (count > CoverageInspector.MaximumCollectionItems)
                    {
                        warnings.Add("pruned-collection", $"{path}: {reader.Path} is beyond the audit limit of {CoverageInspector.MaximumCollectionItems} entries");
                        return true;
                    }
                }
                if (reader.TokenType == JsonToken.StartArray) containers.Push(0);
                else if (reader.TokenType == JsonToken.StartObject) containers.Push(-1);
            }
            if (containers.Count > 0)
            {
                warnings.Add("malformed saved JSON", path + ": output ends inside a JSON container");
                return true;
            }
            return false;
        }
        catch (JsonReaderException ex)
        {
            warnings.Add("malformed saved JSON", path + ": " + ex.Message.Split('\n')[0]);
            return true;
        }
    }

    sealed class HashCommentReader(TextReader input) : TextReader
    {
        bool lineStart = true, secondSlash;
        public override int Read()
        {
            if (secondSlash) { secondSlash = false; return '/'; }
            int value = input.Read();
            if (lineStart && value == '#') { lineStart = false; secondSlash = true; return '/'; }
            lineStart = value == '\n';
            return value;
        }
        public override int Read(char[] buffer, int index, int count)
        {
            int copied = 0, value;
            while (copied < count && (value = Read()) >= 0) buffer[index + copied++] = (char)value;
            return copied;
        }
        protected override void Dispose(bool disposing) { if (disposing) input.Dispose(); base.Dispose(disposing); }
    }
}
