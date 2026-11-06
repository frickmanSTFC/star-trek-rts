using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FinalFrontier.Sim.Data;

/// <summary>
/// The release data pack (game/gamedata.pack, made by tools/DataPacker): one binary file holding every data file the
/// game reads, so players can't casually edit the numbers. Layout (little-endian):
/// <code>
///   8 bytes  magic "FFDATA\r\n"
///   2 bytes  pack format version (1)
///   32 bytes data fingerprint (SHA-256, see DataFingerprint)
///   32 bytes SHA-256 of the compressed body
///   4 bytes  body length
///   body     Brotli-compressed: file count (int32), then per file: path, text (each int32 byte length + UTF-8)
/// </code>
/// Reading checks both hashes, so a changed or damaged pack is refused. This deters casual edits only: anyone can
/// rebuild a pack. Multiplayer state hashes are the real check.
/// </summary>
public static class DataPack
{
    public const ushort FormatVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("FFDATA\r\n");
    private const int HeaderSize = 8 + 2 + 32 + 32 + 4;

    /// <summary>Packs file texts by path (normally <see cref="GameData.Sources"/>).</summary>
    public static byte[] Write(IReadOnlyDictionary<string, string> files)
    {
        using var body = new MemoryStream();
        using (var brotli = new BrotliStream(body, CompressionLevel.SmallestSize, leaveOpen: true))
        using (var w = new BinaryWriter(brotli, Encoding.UTF8, leaveOpen: true))
        {
            var paths = files.Keys.OrderBy(p => p, StringComparer.Ordinal).ToList();
            w.Write(paths.Count);
            foreach (var path in paths)
            {
                WriteText(w, path);
                WriteText(w, files[path]);
            }
        }
        var compressed = body.ToArray();
        using var outStream = new MemoryStream();
        using (var w = new BinaryWriter(outStream))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(Convert.FromHexString(DataFingerprint.Of(files)));
            w.Write(SHA256.HashData(compressed));
            w.Write(compressed.Length);
            w.Write(compressed);
        }
        return outStream.ToArray();
    }

    /// <summary>The file texts in a pack; throws <see cref="InvalidDataException"/> if it isn't one or was changed.</summary>
    public static IReadOnlyDictionary<string, string> Read(byte[] pack, out string fingerprint)
    {
        if (pack.Length < HeaderSize || !pack.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a Final Frontier data pack.");
        var version = BitConverter.ToUInt16(pack, 8);
        if (version != FormatVersion) throw new InvalidDataException($"Data pack format {version} isn't supported (this build reads {FormatVersion}).");
        fingerprint = Convert.ToHexString(pack, 10, 32).ToLowerInvariant();
        var bodyHash = pack.AsSpan(42, 32);
        var length = BitConverter.ToInt32(pack, 74);
        if (length < 0 || HeaderSize + length != pack.Length) throw new InvalidDataException("The data pack is truncated or has extra bytes.");
        var compressed = pack.AsSpan(HeaderSize, length).ToArray();
        if (!SHA256.HashData(compressed).AsSpan().SequenceEqual(bodyHash)) throw new InvalidDataException("The data pack was changed or is damaged (body hash).");
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var brotli = new BrotliStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var r = new BinaryReader(brotli, Encoding.UTF8);
            var count = r.ReadInt32();
            for (var i = 0; i < count; i++) files[ReadText(r)] = ReadText(r);
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException)
        {
            throw new InvalidDataException("The data pack is damaged (body).", e);
        }
        if (DataFingerprint.Of(files) != fingerprint) throw new InvalidDataException("The data pack was changed (fingerprint).");
        return files;
    }

    private static void WriteText(BinaryWriter w, string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        w.Write(bytes.Length);
        w.Write(bytes);
    }

    private static string ReadText(BinaryReader r)
    {
        var length = r.ReadInt32();
        if (length < 0 || length > 64 << 20) throw new InvalidDataException("The data pack is damaged (length).");
        var bytes = r.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }
}
