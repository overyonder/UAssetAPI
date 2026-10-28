using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace UAssetAPI;

/// <summary>A concrete decode condition reported with coverage output.</summary>
public sealed class DecodeCoverageDiagnostic
{
    public string Kind;
    public string Detail;
    public long Count;
}

/// <summary>Per-package decode conditions, retaining an occurrence count and one concrete example per kind.</summary>
public sealed class DecodeCoverage
{
    readonly Dictionary<string, DecodeCoverageDiagnostic> diagnostics = new();
    public IEnumerable<DecodeCoverageDiagnostic> Diagnostics => diagnostics.Values;
    internal void Clear() => diagnostics.Clear();

    public void Report(string kind, string detail) => Report(kind, detail, 1);

    internal void Include(IEnumerable<DecodeCoverageDiagnostic> values)
    {
        foreach (var value in values) Report(value.Kind, value.Detail, value.Count);
    }

    void Report(string kind, string detail, long count)
    {
        if (diagnostics.TryGetValue(kind, out var diagnostic)) diagnostic.Count += count;
        else diagnostics.Add(kind, new DecodeCoverageDiagnostic { Kind = kind, Detail = detail, Count = count });
    }
}

// Seeks do not count as reads. Coalesce adjacent reads, then union ranges before reporting actual holes.
// The stream borrows its input; ownership remains with the caller's AssetBinaryReader.
internal sealed class CoverageReadStream(Stream input, DecodeCoverage coverage) : Stream
{
    readonly List<(long Start, long End)> ranges = new();
    internal (int Count, (long Start, long End) Last) Checkpoint() => (ranges.Count, ranges.Count == 0 ? default : ranges[^1]);

    internal void Rollback((int Count, (long Start, long End) Last) checkpoint)
    {
        if (ranges.Count > checkpoint.Count) ranges.RemoveRange(checkpoint.Count, ranges.Count - checkpoint.Count);
        if (checkpoint.Count > 0) ranges[^1] = checkpoint.Last;
    }
    void Record(long start, int count)
    {
        if (count <= 0) return;
        long end = checked(start + count);
        if (ranges.Count > 0 && start >= ranges[^1].Start && start <= ranges[^1].End)
            ranges[^1] = (ranges[^1].Start, Math.Max(ranges[^1].End, end));
        else ranges.Add((start, end));
    }

    internal void ReportUnreadRanges()
    {
        long consumed = 0;
        foreach (var range in ranges.OrderBy(range => range.Start))
        {
            if (range.Start > consumed) ReportHole(consumed, range.Start);
            consumed = Math.Max(consumed, range.End);
        }
        if (consumed < Length) ReportHole(consumed, Length);
    }

    void ReportHole(long start, long end)
    {
        // SavePackage2 writes PACKAGE_FILE_TAG after exports and additional files. Validate an isolated final tag.
        if (end == Length && end - start == 4)
        {
            long previous = input.Position;
            Span<byte> tag = stackalloc byte[4];
            input.Position = start;
            input.ReadExactly(tag);
            input.Position = previous;
            if (System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(tag) == UAsset.UASSET_MAGIC) return;
        }
        coverage.Report("unread-source", $"Package bytes [{start}, {end}) were not interpreted ({end - start} bytes).");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long start = Position;
        int read = input.Read(buffer, offset, count);
        Record(start, read);
        return read;
    }
    public override int Read(Span<byte> buffer)
    {
        long start = Position;
        int read = input.Read(buffer);
        Record(start, read);
        return read;
    }
    public override int ReadByte()
    {
        long start = Position;
        int value = input.ReadByte();
        if (value >= 0) Record(start, 1);
        return value;
    }
    public override bool CanRead => input.CanRead;
    public override bool CanSeek => input.CanSeek;
    public override bool CanWrite => false;
    public override long Length => input.Length;
    public override long Position { get => input.Position; set => input.Position = value; }
    public override long Seek(long offset, SeekOrigin origin) => input.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
