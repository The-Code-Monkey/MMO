using System;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// The "where was this player last standing" record. A plain JSON DTO: the client saves the
    /// zone transport address and port it successfully connected to under its own Nakama account
    /// (collection <c>player_metadata</c>, key <c>last_zone</c>) and the Main Menu reads it back on
    /// the next login, so joining boots the player straight back into that zone.
    ///
    /// <para>
    /// <see cref="ip"/> holds a host address — an IP or a resolvable name. The field is named
    /// "ip" because that is what the login response exposes, but the value is whatever the Tugboat
    /// transport was pointed at, so a deployable hostname works unchanged.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class LastZoneInfo
    {
        /// <summary>An address that is never a real server; used to flag "unset".</summary>
        public const string UnspecifiedAddress = "0.0.0.0";

        /// <summary>The zone's host address (IP or resolvable name).</summary>
        public string Ip;

        /// <summary>The zone's transport port.</summary>
        public int Port;

        /// <summary>ISO-8601 UTC timestamp of when this zone was recorded, for diagnostics.</summary>
        public string SavedAt;

        public LastZoneInfo()
        {
        }

        public LastZoneInfo(string ip, int port, string savedAt = null)
        {
            Ip = ip;
            Port = port;
            SavedAt = savedAt;
        }

        /// <summary>
        /// A usable zone: it names a real host on a real port. Covers the DTO's "nothing was ever
        /// saved" state — a null object, a blank record, and the explicitly "unset" address are
        /// all equally invalid and mean "use the default zone".
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Ip) || string.Equals(Ip, UnspecifiedAddress, StringComparison.Ordinal))
                    return false;

                return Port is > 0 and <= ushort.MaxValue;
            }
        }

        public static LastZoneInfo Create(string ip, int port, DateTime? savedAt = null)
        {
            string stamp = savedAt?.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                ?? DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            return new LastZoneInfo(ip, port, stamp);
        }

        public string ToJson() => JsonUtility.ToJson(this);

        /// <summary>
        /// Parses a stored zone. Tolerant by design: a record the backend kept for another version
        /// of the game must never take the login down, so malformed payloads and blanks all read as
        /// "no zone" (null) rather than throwing.
        /// </summary>
        public static LastZoneInfo FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                return JsonUtility.FromJson<LastZoneInfo>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public override string ToString()
            => IsValid ? $"{Ip}:{Port}" : "(no zone)";
    }
}