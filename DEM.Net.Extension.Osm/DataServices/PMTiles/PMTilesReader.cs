using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace DEM.Net.Extension.Osm.PMTiles
{
    public enum PMTilesCompression : byte { Unknown = 0, None = 1, Gzip = 2, Brotli = 3, Zstd = 4 }

    public enum PMTilesTileType : byte { Unknown = 0, Mvt = 1, Png = 2, Jpeg = 3, Webp = 4, Avif = 5 }

    /// <summary>
    /// PMTiles v3 fixed-size header (127 bytes). See https://github.com/protomaps/PMTiles/blob/main/spec/v3/spec.md
    /// </summary>
    public sealed class PMTilesHeader
    {
        public const int Size = 127;

        public ulong RootDirectoryOffset { get; private set; }
        public ulong RootDirectoryLength { get; private set; }
        public ulong MetadataOffset { get; private set; }
        public ulong MetadataLength { get; private set; }
        public ulong LeafDirectoriesOffset { get; private set; }
        public ulong LeafDirectoriesLength { get; private set; }
        public ulong TileDataOffset { get; private set; }
        public ulong TileDataLength { get; private set; }
        public ulong AddressedTilesCount { get; private set; }
        public ulong TileEntriesCount { get; private set; }
        public ulong TileContentsCount { get; private set; }
        public bool Clustered { get; private set; }
        public PMTilesCompression InternalCompression { get; private set; }
        public PMTilesCompression TileCompression { get; private set; }
        public PMTilesTileType TileType { get; private set; }
        public int MinZoom { get; private set; }
        public int MaxZoom { get; private set; }
        public double MinLon { get; private set; }
        public double MinLat { get; private set; }
        public double MaxLon { get; private set; }
        public double MaxLat { get; private set; }
        public int CenterZoom { get; private set; }
        public double CenterLon { get; private set; }
        public double CenterLat { get; private set; }

        public static PMTilesHeader Parse(ReadOnlySpan<byte> b)
        {
            if (b.Length < Size) throw new InvalidDataException("PMTiles header is truncated.");
            if (!b.Slice(0, 7).SequenceEqual("PMTiles"u8)) throw new InvalidDataException("Not a PMTiles archive (bad magic number).");
            if (b[7] != 3) throw new NotSupportedException($"PMTiles spec version {b[7]} is not supported (only v3).");

            return new PMTilesHeader
            {
                RootDirectoryOffset = U64(b, 8),
                RootDirectoryLength = U64(b, 16),
                MetadataOffset = U64(b, 24),
                MetadataLength = U64(b, 32),
                LeafDirectoriesOffset = U64(b, 40),
                LeafDirectoriesLength = U64(b, 48),
                TileDataOffset = U64(b, 56),
                TileDataLength = U64(b, 64),
                AddressedTilesCount = U64(b, 72),
                TileEntriesCount = U64(b, 80),
                TileContentsCount = U64(b, 88),
                Clustered = b[96] == 1,
                InternalCompression = (PMTilesCompression)b[97],
                TileCompression = (PMTilesCompression)b[98],
                TileType = (PMTilesTileType)b[99],
                MinZoom = b[100],
                MaxZoom = b[101],
                MinLon = E7(b, 102),
                MinLat = E7(b, 106),
                MaxLon = E7(b, 110),
                MaxLat = E7(b, 114),
                CenterZoom = b[118],
                CenterLon = E7(b, 119),
                CenterLat = E7(b, 123),
            };
        }

        private static ulong U64(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadUInt64LittleEndian(b.Slice(offset, 8));
        private static double E7(ReadOnlySpan<byte> b, int offset) => BinaryPrimitives.ReadInt32LittleEndian(b.Slice(offset, 4)) / 10_000_000d;

        public override string ToString() =>
            $"PMTiles v3: {TileType}, z{MinZoom}-{MaxZoom}, tiles={AddressedTilesCount:N0}, compression(tiles={TileCompression}, internal={InternalCompression}), bounds=[{MinLon},{MinLat},{MaxLon},{MaxLat}]";
    }

    internal struct PMTilesDirectoryEntry
    {
        public ulong TileId;
        public ulong Offset;
        public uint Length;
        public uint RunLength; // 0 => entry points to a leaf directory
    }

    /// <summary>
    /// Thread-safe random access reader for a local PMTiles v3 archive.
    /// Uses positional reads (no shared stream position), so a single instance can serve concurrent requests.
    /// </summary>
    public sealed class PMTilesReader : IDisposable
    {
        private const int MaxDirectoryDepth = 4; // spec: root + up to 3 leaf levels
        private const int MaxCachedLeafDirectories = 4096;

        private readonly SafeFileHandle _handle;
        private readonly PMTilesDirectoryEntry[] _rootDirectory;
        private readonly ConcurrentDictionary<ulong, PMTilesDirectoryEntry[]> _leafCache = new ConcurrentDictionary<ulong, PMTilesDirectoryEntry[]>();

        public string FilePath { get; }
        public PMTilesHeader Header { get; }

        public PMTilesReader(string filePath)
        {
            FilePath = filePath;
            _handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, FileOptions.RandomAccess);
            try
            {
                Header = PMTilesHeader.Parse(ReadBytes(0, PMTilesHeader.Size));
                _rootDirectory = ReadDirectory(Header.RootDirectoryOffset, Header.RootDirectoryLength);
            }
            catch
            {
                _handle.Dispose();
                throw;
            }
        }

        /// <summary>Returns the archive JSON metadata (vector_layers, attribution, version...).</summary>
        public string ReadMetadataJson()
        {
            if (Header.MetadataLength == 0) return "{}";
            var bytes = Decompress(ReadBytes(Header.MetadataOffset, Header.MetadataLength), Header.InternalCompression);
            return Encoding.UTF8.GetString(bytes);
        }

        /// <summary>
        /// Returns the decompressed tile payload, or null when the tile is not present in the archive.
        /// </summary>
        public byte[] GetTile(int z, int x, int y)
        {
            ulong tileId = PMTilesTileId.FromZxy(z, x, y);
            PMTilesDirectoryEntry[] directory = _rootDirectory;

            for (int depth = 0; depth < MaxDirectoryDepth; depth++)
            {
                if (!TryFindEntry(directory, tileId, out PMTilesDirectoryEntry entry))
                    return null;

                if (entry.RunLength > 0)
                {
                    byte[] data = ReadBytes(Header.TileDataOffset + entry.Offset, entry.Length);
                    return Decompress(data, Header.TileCompression);
                }

                directory = GetLeafDirectory(Header.LeafDirectoriesOffset + entry.Offset, entry.Length);
            }

            throw new InvalidDataException($"PMTiles directory depth exceeded {MaxDirectoryDepth} while looking up tile {z}/{x}/{y}.");
        }

        private PMTilesDirectoryEntry[] GetLeafDirectory(ulong offset, ulong length)
        {
            if (_leafCache.TryGetValue(offset, out var cached)) return cached;

            var directory = ReadDirectory(offset, length);
            if (_leafCache.Count >= MaxCachedLeafDirectories) _leafCache.Clear(); // crude bound, leaves are cheap to re-read
            _leafCache[offset] = directory;
            return directory;
        }

        private PMTilesDirectoryEntry[] ReadDirectory(ulong offset, ulong length)
            => DeserializeDirectory(Decompress(ReadBytes(offset, length), Header.InternalCompression));

        private byte[] ReadBytes(ulong offset, ulong length)
        {
            if (length > int.MaxValue) throw new InvalidDataException($"PMTiles block too large: {length} bytes.");
            var buffer = new byte[(int)length];
            int read = 0;
            while (read < buffer.Length)
            {
                int n = RandomAccess.Read(_handle, buffer.AsSpan(read), checked((long)offset + read));
                if (n == 0) throw new EndOfStreamException($"Unexpected end of PMTiles file at offset {offset + (ulong)read}.");
                read += n;
            }
            return buffer;
        }

        internal static PMTilesDirectoryEntry[] DeserializeDirectory(byte[] data)
        {
            int pos = 0;
            int count = checked((int)Varint.Read(data, ref pos));
            var entries = new PMTilesDirectoryEntry[count];

            ulong lastId = 0;
            for (int i = 0; i < count; i++)
            {
                lastId += Varint.Read(data, ref pos);
                entries[i].TileId = lastId;
            }
            for (int i = 0; i < count; i++) entries[i].RunLength = checked((uint)Varint.Read(data, ref pos));
            for (int i = 0; i < count; i++) entries[i].Length = checked((uint)Varint.Read(data, ref pos));
            for (int i = 0; i < count; i++)
            {
                ulong v = Varint.Read(data, ref pos);
                entries[i].Offset = (v == 0 && i > 0)
                    ? entries[i - 1].Offset + entries[i - 1].Length
                    : v - 1;
            }
            return entries;
        }

        internal static bool TryFindEntry(PMTilesDirectoryEntry[] entries, ulong tileId, out PMTilesDirectoryEntry entry)
        {
            int lo = 0, hi = entries.Length - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                ulong midId = entries[mid].TileId;
                if (tileId > midId) lo = mid + 1;
                else if (tileId < midId) hi = mid - 1;
                else { entry = entries[mid]; return true; }
            }

            // hi is now the last entry with TileId < tileId
            if (hi >= 0)
            {
                var candidate = entries[hi];
                if (candidate.RunLength == 0 || tileId - candidate.TileId < candidate.RunLength)
                {
                    entry = candidate;
                    return true;
                }
            }
            entry = default;
            return false;
        }

        internal static byte[] Decompress(byte[] data, PMTilesCompression compression)
        {
            switch (compression)
            {
                case PMTilesCompression.None:
                    return data;
                case PMTilesCompression.Unknown:
                    return data.Length > 2 && data[0] == 0x1f && data[1] == 0x8b ? Inflate(new GZipStream(new MemoryStream(data), CompressionMode.Decompress)) : data;
                case PMTilesCompression.Gzip:
                    return Inflate(new GZipStream(new MemoryStream(data), CompressionMode.Decompress));
                case PMTilesCompression.Brotli:
                    return Inflate(new BrotliStream(new MemoryStream(data), CompressionMode.Decompress));
                case PMTilesCompression.Zstd:
                    throw new NotSupportedException("Zstd-compressed PMTiles are not supported. Protomaps builds use gzip; re-encode the archive or add a zstd decoder.");
                default:
                    throw new NotSupportedException($"Unknown PMTiles compression {(byte)compression}.");
            }
        }

        private static byte[] Inflate(Stream decompressor)
        {
            using (decompressor)
            using (var ms = new MemoryStream())
            {
                decompressor.CopyTo(ms);
                return ms.ToArray();
            }
        }

        public void Dispose() => _handle.Dispose();
    }

    /// <summary>
    /// PMTiles tile id: tiles of all lower zooms are counted first, then position along a Hilbert curve at zoom z.
    /// </summary>
    public static class PMTilesTileId
    {
        public static ulong FromZxy(int z, int x, int y)
        {
            if (z < 0 || z > 26) throw new ArgumentOutOfRangeException(nameof(z), "Zoom must be in [0, 26].");
            long n = 1L << z;
            if (x < 0 || x >= n || y < 0 || y >= n) throw new ArgumentOutOfRangeException(nameof(x), $"Tile {z}/{x}/{y} is out of range.");

            ulong acc = ((1UL << (2 * z)) - 1) / 3; // sum of 4^i for i < z
            long tx = x, ty = y;
            ulong d = 0;
            for (long s = n / 2; s > 0; s /= 2)
            {
                long rx = (tx & s) > 0 ? 1 : 0;
                long ry = (ty & s) > 0 ? 1 : 0;
                d += (ulong)(s * s * ((3 * rx) ^ ry));
                if (ry == 0)
                {
                    if (rx == 1)
                    {
                        tx = n - 1 - tx;
                        ty = n - 1 - ty;
                    }
                    (tx, ty) = (ty, tx);
                }
            }
            return acc + d;
        }
    }

    internal static class Varint
    {
        public static ulong Read(byte[] data, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= data.Length) throw new InvalidDataException("Truncated varint.");
                byte b = data[pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return result;
                shift += 7;
                if (shift >= 64) throw new InvalidDataException("Varint too long.");
            }
        }
    }
}
