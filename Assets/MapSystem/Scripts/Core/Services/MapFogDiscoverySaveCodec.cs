using System;

namespace Artifika.Map.Core
{
    public enum MapFogScopeType
    {
        Player = 0,
        Party = 1,
        Team = 2,
        World = 3
    }

    public readonly struct MapFogScopeKey : IEquatable<MapFogScopeKey>
    {
        public MapFogScopeKey(MapFogScopeType type, string id)
        {
            Type = type;
            Id = string.IsNullOrWhiteSpace(id) ? DefaultIdFor(type) : id;
        }

        public MapFogScopeType Type { get; }
        public string Id { get; }

        public bool Equals(MapFogScopeKey other)
        {
            return Type == other.Type && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MapFogScopeKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Type * 397) ^ (Id != null ? StringComparer.Ordinal.GetHashCode(Id) : 0);
            }
        }

        public override string ToString()
        {
            return $"{Type}:{Id}";
        }

        public string ToSaveKeySegment()
        {
            return Type == MapFogScopeType.Player
                ? Id
                : $"{Type}_{Id}";
        }

        public static string DefaultIdFor(MapFogScopeType type)
        {
            return type == MapFogScopeType.World ? "World" : "Default";
        }
    }

    [Serializable]
    public sealed class MapFogDiscoverySaveData
    {
        public int Version;
        public int ScopeType;
        public string ScopeId;
        public int Resolution;
        public int Encoding;
        public int UncompressedLength;
        public int Checksum;       // FNV-1a, stored unchecked as int for serializer support
        public string Payload;     // Base64 of the compressed mask
    }

    public static class MapFogDiscoverySaveCodec
    {
        public const int CurrentSaveDataVersion = 3;
        private const int LegacySaveDataVersion = 2;

        public static MapFogDiscoverySaveData Encode(MapFogMaskSnapshot snapshot, int resolution)
        {
            return Encode(snapshot, resolution, new MapFogScopeKey(MapFogScopeType.Player, MapFogScopeKey.DefaultIdFor(MapFogScopeType.Player)));
        }

        public static MapFogDiscoverySaveData Encode(
            MapFogMaskSnapshot snapshot,
            int resolution,
            MapFogScopeKey scopeKey)
        {
            return new MapFogDiscoverySaveData
            {
                Version = CurrentSaveDataVersion,
                ScopeType = (int)scopeKey.Type,
                ScopeId = scopeKey.Id,
                Resolution = resolution,
                Encoding = snapshot.Encoding,
                UncompressedLength = snapshot.UncompressedLength,
                Checksum = unchecked((int)snapshot.Checksum),
                Payload = Convert.ToBase64String(snapshot.Payload)
            };
        }

        public static bool TryDecode(MapFogDiscoverySaveData data, out int resolution, out byte[] mask)
        {
            return TryDecode(data, out resolution, out mask, out _);
        }

        public static bool TryDecode(
            MapFogDiscoverySaveData data,
            out int resolution,
            out byte[] mask,
            out MapFogScopeKey scopeKey)
        {
            resolution = 0;
            mask = null;
            scopeKey = default;

            if (data == null ||
                (data.Version != CurrentSaveDataVersion && data.Version != LegacySaveDataVersion) ||
                string.IsNullOrEmpty(data.Payload) ||
                !MapFogMaskCodec.TryGetMaskByteCount(data.Resolution, out int expectedMaskLength) ||
                data.UncompressedLength != expectedMaskLength)
            {
                return false;
            }

            if (data.Version == CurrentSaveDataVersion)
            {
                if (!Enum.IsDefined(typeof(MapFogScopeType), data.ScopeType))
                    return false;

                var scopeType = (MapFogScopeType)data.ScopeType;
                scopeKey = new MapFogScopeKey(scopeType, data.ScopeId);
            }

            byte[] payload;
            try
            {
                payload = Convert.FromBase64String(data.Payload);
            }
            catch (FormatException)
            {
                return false;
            }

            if (!MapFogMaskCodec.TryDecode(
                    data.Encoding,
                    payload,
                    data.UncompressedLength,
                    unchecked((uint)data.Checksum),
                    out mask))
            {
                return false;
            }

            resolution = data.Resolution;
            return true;
        }
    }
}
