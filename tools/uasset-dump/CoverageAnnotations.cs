using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Add diagnostics without reserializing the saved body. Preserve prior concrete diagnostics and verify each replacement.
static class CoverageAnnotations
{
    public static bool HasWarning(string path)
    {
        using var reader = new StreamReader(path);
        char[] prefix = new char[4096];
        int count = reader.Read(prefix, 0, prefix.Length);
        string text = new string(prefix, 0, count);
        if (text.StartsWith("# WARNING: " + CoverageWarnings.Notice + "\n# ")) return true;
        return System.Text.RegularExpressions.Regex.IsMatch(text, "^\\{\\s*\"CoverageWarnings\"\\s*:\\s*\\[\\s*\"" + System.Text.RegularExpressions.Regex.Escape(CoverageWarnings.Notice) + "\"\\s*,\\s*\"");
    }

    public static bool Apply(string path, CoverageWarnings warnings)
    {
        if (!warnings.Any) return false;
        var link = new FileInfo(path);
        if (link.LinkTarget != null) path = link.ResolveLinkTarget(true)?.FullName ?? throw new IOException("Broken output symlink: " + path);
        var info = new FileInfo(path);
        long size = info.Length;
        var modified = info.LastWriteTimeUtc;
        var mode = OperatingSystem.IsWindows() ? default : File.GetUnixFileMode(path);
        string temporary = path + ".coverage-" + Guid.NewGuid().ToString("N");
        byte[] buffer = new byte[131072];
        try
        {
            using var input = File.OpenRead(path);
            int read = input.Read(buffer, 0, buffer.Length);
            string prefix = Encoding.UTF8.GetString(buffer, 0, read);
            var details = new HashSet<string>(warnings.Details, StringComparer.Ordinal);
            int start = 0, end = 0;
            bool json = prefix.TrimStart('\uFEFF', ' ', '\r', '\n', '\t').StartsWith("{");
            if (!json && prefix.StartsWith("# WARNING: " + CoverageWarnings.Notice + "\n"))
            {
                while (end < read && buffer[end] == '#' && end + 1 < read && buffer[end + 1] == ' ')
                {
                    int newline = Array.IndexOf(buffer, (byte)'\n', end, read - end);
                    if (newline < 0) throw new InvalidDataException("Warning header exceeds buffer");
                    if (end > 0) details.Add(Encoding.UTF8.GetString(buffer, end + 2, newline - end - 2));
                    end = newline + 1;
                }
            }
            else if (json)
            {
                int brace = Array.IndexOf(buffer, (byte)'{', 0, read);
                start = end = brace + 1;
                var reader = new Utf8JsonReader(buffer.AsSpan(brace, read - brace), read == size, default);
                if (!reader.Read() || !reader.Read()) throw new InvalidDataException("Missing JSON root");
                if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("CoverageWarnings"))
                {
                    if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray) throw new InvalidDataException("Invalid warning array");
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType != JsonTokenType.String) throw new InvalidDataException("Non-string warning");
                        string detail = reader.GetString();
                        if (detail != CoverageWarnings.Notice) details.Add(detail);
                    }
                    if (reader.TokenType != JsonTokenType.EndArray) throw new InvalidDataException("Incomplete warning array");
                    end = brace + checked((int)reader.BytesConsumed);
                    while (end < read && char.IsWhiteSpace((char)buffer[end])) end++;
                    if (end >= read || buffer[end++] != ',') throw new InvalidDataException("Warning lacks trailing comma");
                }
            }
            byte[] replacement = Encoding.UTF8.GetBytes(json
                ? "\n\"CoverageWarnings\":" + JsonSerializer.Serialize(new[] { CoverageWarnings.Notice }.Concat(details)) + ","
                : "# WARNING: " + CoverageWarnings.Notice + "\n" + string.Concat(details.Select(detail => "# " + detail + "\n")));
            using var expected = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                output.Write(buffer, 0, start); expected.AppendData(buffer, 0, start);
                output.Write(replacement); expected.AppendData(replacement);
                input.Position = end;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    output.Write(buffer, 0, read); expected.AppendData(buffer, 0, read);
                }
            }
            using (var verify = File.OpenRead(temporary))
                if (!SHA256.HashData(verify).AsSpan().SequenceEqual(expected.GetHashAndReset())) throw new IOException("Replacement checksum differs");
            info.Refresh();
            if (info.Length != size || info.LastWriteTimeUtc != modified) throw new IOException("Output changed concurrently; original retained");
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, mode);
            File.Move(temporary, path, true);
            return true;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
