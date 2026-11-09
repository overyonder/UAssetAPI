using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

// Counts reasons, retaining only one example per reason rather than every affected object.
sealed class CoverageWarnings : TextWriter
{
    public const string Notice = "No complete coverage can be claimed based on this output.";
    readonly Dictionary<string, (long Count, string Example)> reasons = new();
    readonly TextWriter records;
    readonly CoverageWarnings summary;
    public int RecordCount { get; private set; }
    public override Encoding Encoding => Encoding.UTF8;

    public CoverageWarnings(TextWriter records = null, CoverageWarnings summary = null)
    {
        this.records = records;
        this.summary = summary;
    }

    public CoverageWarnings Fork()
    {
        var copy = new CoverageWarnings(summary: summary);
        foreach (var pair in reasons) copy.reasons.Add(pair.Key, pair.Value);
        return copy;
    }

    public void Add(string reason, string example, long count = 1)
    {
        lock (reasons)
            reasons[reason] = reasons.TryGetValue(reason, out var entry)
                ? (checked(entry.Count + count), entry.Example) : (count, example);
        summary?.Add(reason, example, count);
    }

    public override void WriteLine(string value)
    {
        records?.WriteLine(value);
        RecordCount++;
        int separator = value.IndexOf('\t');
        string reason = separator < 0 ? "audit diagnostic" : value[..separator];
        Add(reason == "payload" ? "labelled payloads emitted or retained without decoding" : reason,
            value.Replace('\t', ' '));
    }

    public void WriteHeader(TextWriter writer)
    {
        if (reasons.Count == 0) return;
        writer.WriteLine("# WARNING: " + Notice);
        foreach (var (reason, entry) in reasons)
            writer.WriteLine($"# {reason} ({entry.Count}): {entry.Example}");
    }

    public IEnumerable<string> Details => reasons.Select(pair => $"{pair.Key} ({pair.Value.Count}): {pair.Value.Example}");
    public bool Any => reasons.Count > 0;

    // Render before writing the header: formatters can discover omissions while producing the body.
    // Both passes stream through disk, keeping the memory bound independent of output size.
    public bool WriteFile(string path, Func<TextWriter, bool> render, bool json = false)
    {
        string body = Path.GetTempFileName();
        try
        {
            bool any;
            using (var writer = new StreamWriter(body)) any = render(writer);
            WriteStagedBody(path, body, json);
            return any;
        }
        catch (Exception ex)
        {
            Add("output failed", (path ?? "stdout") + ": " + ex.Message.Split('\n')[0]);
            throw;
        }
        finally { File.Delete(body); }
    }

    public void WriteStagedBody(string path, string body, bool json = false)
    {
        string output = path == null ? null : path + ".coverage-" + Guid.NewGuid().ToString("N");
        try
        {
            using var reader = new StreamReader(body);
            using (var writer = output == null ? new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true } : new StreamWriter(output))
            {
                if (json && reasons.Count > 0)
                {
                    int first;
                    do { first = reader.Read(); } while (first >= 0 && char.IsWhiteSpace((char)first));
                    if (first != '{') throw new InvalidDataException("Expected a package JSON object.");
                    writer.Write("{\n\"CoverageWarnings\": [");
                    writer.Write(JsonConvert.SerializeObject(Notice));
                    foreach (var (reason, entry) in reasons)
                        writer.Write("," + JsonConvert.SerializeObject($"{reason} ({entry.Count}): {entry.Example}"));
                    writer.Write("],");
                }
                else if (!json) WriteHeader(writer);
                char[] buffer = new char[65536];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0) writer.Write(buffer, 0, read);
            }
            if (output != null) File.Move(output, path, true);
        }
        finally { if (output != null) File.Delete(output); }
    }


}

sealed class CoverageWarningSummary : IDisposable
{
    public readonly CoverageWarnings Warnings = new();
    public void Dispose() => Warnings.WriteHeader(Console.Error);
}

// Diagnostics can be redirected to files too. Stage their body so findings discovered
// anywhere in the invocation precede the first output record, as they do for dump files.
sealed class CoverageDiagnosticOutput : IDisposable
{
    readonly CoverageWarnings warnings;
    readonly TextWriter previous;
    readonly string body;
    readonly StreamWriter writer;

    internal CoverageDiagnosticOutput(CoverageWarnings warnings, bool enabled)
    {
        this.warnings = warnings;
        if (!enabled) return;
        previous = Console.Out;
        body = Path.GetTempFileName();
        writer = new StreamWriter(body);
        Console.SetOut(writer);
    }

    public void Dispose()
    {
        if (writer == null) return;
        Console.SetOut(previous);
        try
        {
            writer.Dispose();
            warnings.WriteStagedBody(null, body);
        }
        finally { File.Delete(body); }
    }
}
