using System;
using System.Runtime.InteropServices;

namespace UAssetAPI;

/// <summary>
/// Oodle decompression through the oo2core shared library, which UAssetAPI does not ship. UASSET_OODLE names its path
/// (the engine carries one, for example under Engine/Binaries/DotNET/AutomationTool/.../runtimes/linux-x64/native);
/// without it the library is looked up by name.
/// </summary>
public static class Oodle
{
    unsafe delegate long DecompressFn(byte* compBuf, long compBufSize, byte* rawBuf, long rawLen, int fuzzSafe, int checkCRC,
        int verbosity, byte* decBufBase, long decBufSize, IntPtr fpCallback, IntPtr callbackUserData, byte* decoderMemory,
        long decoderMemorySize, int threadPhase);

    static readonly Lazy<DecompressFn> decompress = new(() =>
    {
        string path = Environment.GetEnvironmentVariable("UASSET_OODLE");
        IntPtr library = !string.IsNullOrEmpty(path) ? NativeLibrary.Load(path)
            : NativeLibrary.TryLoad("liboo2core.so", out var byName) ? byName
            : NativeLibrary.TryLoad("oo2core", out var byShortName) ? byShortName
            : throw new DllNotFoundException("Oodle (oo2core) not found; set UASSET_OODLE to its path");
        return Marshal.GetDelegateForFunctionPointer<DecompressFn>(NativeLibrary.GetExport(library, "OodleLZ_Decompress"));
    });

    /// <summary>OodleLZ_Decompress into a buffer of the known uncompressed size; throws unless every byte is produced.</summary>
    public static unsafe byte[] Decompress(byte[] compressed, int uncompressedSize)
    {
        var output = new byte[uncompressedSize];
        long produced;
        fixed (byte* src = compressed)
        fixed (byte* dst = output)
            produced = decompress.Value(src, compressed.Length, dst, uncompressedSize, 1, 0, 0, null, 0, IntPtr.Zero, IntPtr.Zero, null, 0, 3);
        if (produced != uncompressedSize) throw new FormatException($"Oodle produced {produced} of {uncompressedSize} bytes");
        return output;
    }
}
