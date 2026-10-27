using System;
using System.Collections.Generic;
using System.IO;
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

    public void Add(string reason, string example)
    {
        reasons[reason] = reasons.TryGetValue(reason, out var entry)
            ? (entry.Count + 1, entry.Example) : (1, example);
        summary?.Add(reason, example);
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

    // Keep standalone JSON valid: put the warning first, alongside the existing asset properties.
    public void WriteJson(TextWriter writer, string json)
    {
        writer.Write("{\n\"CoverageWarnings\": [");
        writer.Write(JsonConvert.SerializeObject(Notice));
        foreach (var (reason, entry) in reasons)
        {
            writer.Write(',');
            writer.Write(JsonConvert.SerializeObject($"{reason} ({entry.Count}): {entry.Example}"));
        }
        writer.Write("],");
        writer.Write(json.AsSpan(json.IndexOf('{') + 1));
    }
}

sealed class CoverageWarningSummary : IDisposable
{
    public readonly CoverageWarnings Warnings = new();
    public void Dispose() => Warnings.WriteHeader(Console.Error);
}
