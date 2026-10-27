using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace UAssetAPI.Tests;

[TestClass]
public class DecodeCoverageTests
{
    static string Fixture => Path.Combine(AppContext.BaseDirectory, "TestAssets", "TestEditorUE5_8", "BP_PinMatrix.uasset");

    [TestMethod]
    public void StreamingReadReportsConcreteUnreadDataWithoutChangingDecodedExports()
    {
        var tracked = new UAsset(Fixture, customSerializationFlags: CustomSerializationFlags.ReadOnlyStreaming | CustomSerializationFlags.TrackReadCoverage);
        var ordinary = new UAsset(Fixture, customSerializationFlags: CustomSerializationFlags.ReadOnlyStreaming);
        Assert.IsTrue(tracked.Coverage.Diagnostics.Any(d => d.Kind == "unread-source"));
        Assert.IsTrue(tracked.Coverage.Diagnostics.Any(d => d.Kind == "streaming-trailer"));
        Assert.AreEqual(ordinary.Exports.Count, tracked.Exports.Count);
        for (int i = 0; i < ordinary.Exports.Count; i++)
            Assert.AreEqual(ordinary.SerializeJsonObject(ordinary.Exports[i]), tracked.SerializeJsonObject(tracked.Exports[i]));
    }

    [TestMethod]
    public void SummaryReadEstablishesTheSameSkippedTrailerWithoutDecodingExports()
    {
        var summary = new UAsset { CustomSerializationFlags = CustomSerializationFlags.ReadOnlyStreaming };
        using var reader = new AssetBinaryReader(File.OpenRead(Fixture), summary);
        summary.ReadHeader(reader);
        var parsed = new UAsset(Fixture, customSerializationFlags: CustomSerializationFlags.ReadOnlyStreaming);
        CollectionAssert.AreEqual(parsed.Coverage.Diagnostics.Where(d => d.Kind == "streaming-trailer").Select(d => d.Detail).ToArray(),
            summary.Coverage.Diagnostics.Where(d => d.Kind == "streaming-trailer").Select(d => d.Detail).ToArray());
        Assert.IsTrue(summary.Coverage.Diagnostics.Any(d => d.Kind == "streaming-trailer"));
        Assert.IsTrue(summary.Exports == null || summary.Exports.Count == 0);
    }

    [TestMethod]
    public void SkippedExportsAreReportedEvenWhenTheirBytesWereNotRetained()
    {
        var asset = new UAsset(Fixture, customSerializationFlags: CustomSerializationFlags.SkipLoadingExports | CustomSerializationFlags.ReadOnlyStreaming);
        Assert.IsTrue(asset.Coverage.Diagnostics.Any(d => d.Kind == "skipped-export"));
        Assert.IsTrue(asset.Exports.OfType<RawExport>().All(e => e.Data.Length == 0));
    }

    [TestMethod]
    public void TrackingPreservesTheByteExactWriteBack()
    {
        var asset = new UAsset(Fixture, customSerializationFlags: CustomSerializationFlags.TrackReadCoverage);
        CollectionAssert.AreEqual(File.ReadAllBytes(Fixture), asset.WriteData().ToArray());
        Assert.IsFalse(asset.Coverage.Diagnostics.Any(d => d.Kind == "streaming-trailer"));
    }

    [DataTestMethod]
    [DataRow(4, false)]
    [DataRow(5, true)]
    public void PropertyLengthContractReportsOnlyAnActualMismatch(int declaredBytes, bool mismatch)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        using var bytes = new MemoryStream(BitConverter.GetBytes(17));
        using var reader = new AssetBinaryReader(bytes, asset);
        var property = MainSerializer.TypeToClass(FName.DefineDummy(asset, "IntProperty"), FName.DefineDummy(asset, "Fixture"),
            null, null, null, asset, reader, leng: declaredBytes);
        Assert.AreEqual(17, ((IntPropertyData)property).Value);
        Assert.AreEqual(mismatch, asset.Coverage.Diagnostics.Any(d => d.Kind == "property-size-mismatch"));
    }

    [DataTestMethod]
    [DataRow(0.25, false)]
    [DataRow(0.1, true)]
    public void RawTrackNarrowingWarnsOnlyWhenPrecisionWasLost(double value, bool loss)
    {
        var asset = new UAsset(EngineVersion.VER_UE5_8);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
        {
            writer.Write(1);
            for (int a = 0; a < 3; a++)
            {
                int components = a == 1 ? 4 : 3;
                writer.Write(components * sizeof(double)); writer.Write(1);
                for (int i = 0; i < components; i++) writer.Write(a == 0 && i == 0 ? value : 1.0);
            }
        }
        var decode = typeof(NativeClassTails).GetMethod("DecodeRawTracks", BindingFlags.Static | BindingFlags.NonPublic);
        decode.Invoke(null, new object[] { stream.ToArray(), asset });
        Assert.AreEqual(loss, asset.Coverage.Diagnostics.Any(d => d.Kind == "precision-loss"));
    }
}
