using System;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Pure placement rules for a player arriving through a zone gate. The destination reuses the
    /// gate that points back at the source zone, so the player steps out beside the road they came
    /// through instead of at the zone's general spawn point.
    /// </summary>
    public static class ZoneArrivalPlacement
    {
        /// <summary>How far inside the zone from the gate centre the player is placed.</summary>
        public const float GateInset = 3f;

        /// <summary>The gate in this zone whose destination is the zone the player came from.</summary>
        public static ZoneTransferTrigger FindGateBackTo(string sourceServerId, ZoneTransferTrigger[] gates)
        {
            if (string.IsNullOrEmpty(sourceServerId) || gates == null)
                return null;

            foreach (ZoneTransferTrigger gate in gates)
            {
                if (gate != null && string.Equals(gate.TargetServerId, sourceServerId, StringComparison.Ordinal))
                    return gate;
            }

            return null;
        }

        /// <summary>Moves the gate position inward toward the zone centre, keeping its height.</summary>
        public static Vector3 NearGate(Vector3 gatePosition, Vector3 zoneCenter)
        {
            Vector3 inward = zoneCenter - gatePosition;
            inward.y = 0f;

            if (inward.sqrMagnitude < 0.0001f)
                inward = Vector3.back;

            Vector3 position = gatePosition + inward.normalized * GateInset;
            position.y = gatePosition.y;
            return position;
        }
    }
}
