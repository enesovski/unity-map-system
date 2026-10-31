using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace Artifika.Map.Core
{
    public static class MapFogMaskCodec
    {
        public const int DeflateEncoding = 1;
        public const int MinResolution = 64;
        public const int MaxResolution = 1024;
        public const int MaxMaskByteCount = MaxResolution * MaxResolution;
        public const int MaxPayloadByteCount = MaxMaskByteCount + (64 * 1024);

        public static MapFogMaskSnapshot Encode(byte[] mask)
        {
            if (mask == null || mask.Length <= 0 || mask.Length > MaxMaskByteCount)
                throw new ArgumentException("Fog mask has an invalid length.", nameof(mask));

            using var output = new MemoryStream(mask.Length);
            using (var compressor = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(mask, 0, mask.Length);
            }

            return new MapFogMaskSnapshot(
                DeflateEncoding,
                mask.Length,
                ComputeChecksum(mask),
                output.ToArray());
        }

        public static bool TryDecode(
            int encoding,
            byte[] payload,
            int uncompressedLength,
            uint checksum,
            out byte[] mask)
        {
            mask = null;

            if (encoding != DeflateEncoding ||
                payload == null ||
                payload.Length <= 0 ||
                payload.Length > MaxPayloadByteCount ||
                uncompressedLength <= 0 ||
                uncompressedLength > MaxMaskByteCount)
            {
                return false;
            }

            try
            {
                mask = new byte[uncompressedLength];

                using var input = new MemoryStream(payload, writable: false);
                using var decompressor = new DeflateStream(input, CompressionMode.Decompress);

                int offset = 0;
                while (offset < mask.Length)
                {
                    int bytesRead = decompressor.Read(mask, offset, mask.Length - offset);
                    if (bytesRead <= 0)
                    {
                        mask = null;
                        return false;
                    }

                    offset += bytesRead;
                }

                if (decompressor.ReadByte() != -1 || ComputeChecksum(mask) != checksum)
                {
                    mask = null;
                    return false;
                }

                return true;
            }
            catch (InvalidDataException)
            {
                mask = null;
                return false;
            }
            catch (IOException)
            {
                mask = null;
                return false;
            }
        }

        public static bool TryGetMaskByteCount(int resolution, out int byteCount)
        {
            byteCount = 0;
            if (resolution < MinResolution || resolution > MaxResolution)
                return false;

            byteCount = resolution * resolution;
            return true;
        }

        public static bool IsValidPayloadLength(int payloadLength)
        {
            return payloadLength > 0 && payloadLength <= MaxPayloadByteCount;
        }

        private static uint ComputeChecksum(byte[] data)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint checksum = offsetBasis;
            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
                checksum *= prime;
            }

            return checksum;
        }
    }

    public readonly struct MapFogMaskSnapshot
    {
        public MapFogMaskSnapshot(
            int encoding,
            int uncompressedLength,
            uint checksum,
            byte[] payload)
        {
            Encoding = encoding;
            UncompressedLength = uncompressedLength;
            Checksum = checksum;
            Payload = payload;
        }

        public int Encoding { get; }
        public int UncompressedLength { get; }
        public uint Checksum { get; }
        public byte[] Payload { get; }
    }

    public static class MapFogTileDeltaCodec
    {
        public const int DeflateEncoding = 1;
        public const int DefaultTileSize = 16;
        public const int MinTileSize = 8;
        public const int MaxTileSize = 64;
        public const int MaxTilesPerDelta = 64;
        public const int MaxUncompressedDeltaBytes = 96 * 1024;
        public const int MaxPayloadBytes = 128 * 1024;

        private const int TileHeaderBytes = 8;

        public static MapFogTileDeltaSnapshot Encode(
            byte[] mask,
            int resolution,
            int tileSize,
            IReadOnlyList<int> tileIndices)
        {
            if (mask == null ||
                mask.Length != resolution * resolution ||
                !MapFogMaskCodec.TryGetMaskByteCount(resolution, out _) ||
                !IsValidTileSize(tileSize) ||
                tileIndices == null ||
                tileIndices.Count <= 0 ||
                tileIndices.Count > MaxTilesPerDelta)
            {
                throw new ArgumentException("Fog tile delta has invalid inputs.");
            }

            var sortedTiles = new List<int>(tileIndices);
            sortedTiles.Sort();

            using var rawOutput = new MemoryStream();
            using (var writer = new BinaryWriter(rawOutput))
            {
                int previousTile = -1;
                for (int i = 0; i < sortedTiles.Count; i++)
                {
                    int tileIndex = sortedTiles[i];
                    if (tileIndex == previousTile)
                        continue;

                    previousTile = tileIndex;
                    if (!TryGetTileRect(tileIndex, resolution, tileSize, out int tileX, out int tileY, out int width, out int height))
                        throw new ArgumentException("Fog tile index is out of range.");

                    writer.Write((ushort)tileX);
                    writer.Write((ushort)tileY);
                    writer.Write((ushort)width);
                    writer.Write((ushort)height);

                    int startX = tileX * tileSize;
                    int startY = tileY * tileSize;
                    for (int row = 0; row < height; row++)
                    {
                        int sourceOffset = (startY + row) * resolution + startX;
                        writer.Write(mask, sourceOffset, width);
                    }
                }
            }

            byte[] raw = rawOutput.ToArray();
            if (raw.Length <= 0 || raw.Length > MaxUncompressedDeltaBytes)
                throw new ArgumentException("Fog tile delta is too large.");

            using var compressedOutput = new MemoryStream(raw.Length);
            using (var compressor = new DeflateStream(compressedOutput, CompressionLevel.Fastest, leaveOpen: true))
            {
                compressor.Write(raw, 0, raw.Length);
            }

            byte[] payload = compressedOutput.ToArray();
            if (payload.Length <= 0 || payload.Length > MaxPayloadBytes)
                throw new ArgumentException("Fog tile delta payload is too large.");

            return new MapFogTileDeltaSnapshot(
                DeflateEncoding,
                resolution,
                tileSize,
                sortedTiles.Count,
                raw.Length,
                ComputeDeltaChecksum(raw),
                payload);
        }

        public static bool TryApplyDelta(
            int encoding,
            int resolution,
            int tileSize,
            int tileCount,
            int uncompressedLength,
            uint checksum,
            byte[] payload,
            byte[] targetMask,
            bool mergeMax = true)
        {
            if (encoding != DeflateEncoding ||
                targetMask == null ||
                !MapFogMaskCodec.TryGetMaskByteCount(resolution, out int expectedMaskLength) ||
                targetMask.Length != expectedMaskLength ||
                !IsValidTileSize(tileSize) ||
                tileCount <= 0 ||
                tileCount > MaxTilesPerDelta ||
                uncompressedLength <= 0 ||
                uncompressedLength > MaxUncompressedDeltaBytes ||
                payload == null ||
                payload.Length <= 0 ||
                payload.Length > MaxPayloadBytes)
            {
                return false;
            }

            byte[] raw;
            try
            {
                raw = new byte[uncompressedLength];
                using var input = new MemoryStream(payload, writable: false);
                using var decompressor = new DeflateStream(input, CompressionMode.Decompress);

                int offset = 0;
                while (offset < raw.Length)
                {
                    int bytesRead = decompressor.Read(raw, offset, raw.Length - offset);
                    if (bytesRead <= 0)
                        return false;

                    offset += bytesRead;
                }

                if (decompressor.ReadByte() != -1 || ComputeDeltaChecksum(raw) != checksum)
                    return false;
            }
            catch (InvalidDataException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }

            try
            {
                using var rawInput = new MemoryStream(raw, writable: false);
                using var reader = new BinaryReader(rawInput);

                for (int tile = 0; tile < tileCount; tile++)
                {
                    if (rawInput.Length - rawInput.Position < TileHeaderBytes)
                        return false;

                    int tileX = reader.ReadUInt16();
                    int tileY = reader.ReadUInt16();
                    int width = reader.ReadUInt16();
                    int height = reader.ReadUInt16();

                    if (!IsValidTileRect(tileX, tileY, width, height, resolution, tileSize))
                        return false;

                    int startX = tileX * tileSize;
                    int startY = tileY * tileSize;
                    int bytesNeeded = width * height;
                    if (rawInput.Length - rawInput.Position < bytesNeeded)
                        return false;

                    for (int row = 0; row < height; row++)
                    {
                        int targetOffset = (startY + row) * resolution + startX;
                        for (int x = 0; x < width; x++)
                        {
                            byte value = reader.ReadByte();
                            int index = targetOffset + x;
                            targetMask[index] = mergeMax && value < targetMask[index]
                                ? targetMask[index]
                                : value;
                        }
                    }
                }

                return rawInput.Position == rawInput.Length;
            }
            catch (EndOfStreamException)
            {
                return false;
            }
        }

        public static int NormalizeTileSize(int tileSize)
        {
            return Math.Max(MinTileSize, Math.Min(MaxTileSize, tileSize));
        }

        private static bool IsValidTileSize(int tileSize)
        {
            return tileSize >= MinTileSize && tileSize <= MaxTileSize;
        }

        private static int GetTilesPerRow(int resolution, int tileSize)
        {
            return (resolution + tileSize - 1) / tileSize;
        }

        private static bool TryGetTileRect(
            int tileIndex,
            int resolution,
            int tileSize,
            out int tileX,
            out int tileY,
            out int width,
            out int height)
        {
            int tilesPerRow = GetTilesPerRow(resolution, tileSize);
            int tileCount = tilesPerRow * tilesPerRow;

            tileX = 0;
            tileY = 0;
            width = 0;
            height = 0;

            if (tileIndex < 0 || tileIndex >= tileCount)
                return false;

            tileX = tileIndex % tilesPerRow;
            tileY = tileIndex / tilesPerRow;
            int startX = tileX * tileSize;
            int startY = tileY * tileSize;
            width = Math.Min(tileSize, resolution - startX);
            height = Math.Min(tileSize, resolution - startY);
            return width > 0 && height > 0;
        }

        private static bool IsValidTileRect(int tileX, int tileY, int width, int height, int resolution, int tileSize)
        {
            if (tileX < 0 || tileY < 0 || width <= 0 || height <= 0 || width > tileSize || height > tileSize)
                return false;

            int startX = tileX * tileSize;
            int startY = tileY * tileSize;
            return startX < resolution &&
                   startY < resolution &&
                   startX + width <= resolution &&
                   startY + height <= resolution;
        }

        private static uint ComputeDeltaChecksum(byte[] data)
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint checksum = offsetBasis;
            for (int i = 0; i < data.Length; i++)
            {
                checksum ^= data[i];
                checksum *= prime;
            }

            return checksum;
        }
    }

    public readonly struct MapFogTileDeltaSnapshot
    {
        public MapFogTileDeltaSnapshot(
            int encoding,
            int resolution,
            int tileSize,
            int tileCount,
            int uncompressedLength,
            uint checksum,
            byte[] payload)
        {
            Encoding = encoding;
            Resolution = resolution;
            TileSize = tileSize;
            TileCount = tileCount;
            UncompressedLength = uncompressedLength;
            Checksum = checksum;
            Payload = payload;
        }

        public int Encoding { get; }
        public int Resolution { get; }
        public int TileSize { get; }
        public int TileCount { get; }
        public int UncompressedLength { get; }
        public uint Checksum { get; }
        public byte[] Payload { get; }
    }
}
