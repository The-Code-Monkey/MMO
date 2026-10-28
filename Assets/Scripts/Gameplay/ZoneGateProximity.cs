using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>Pure-ish gate proximity queries over a trigger collider.</summary>
    public static class ZoneGateProximity
    {
        public static bool InRange(Collider volume, Vector3 position, float padding)
        {
            if (volume == null)
                return false;

            Vector3 closest = volume.ClosestPoint(position);
            return (closest - position).sqrMagnitude <= padding * padding;
        }

        public static ZoneTransferTrigger FindNearest(
            ZoneTransferTrigger[] gates, Vector3 position, float padding)
        {
            ZoneTransferTrigger best = null;
            float bestDistance = float.PositiveInfinity;

            foreach (ZoneTransferTrigger gate in gates)
            {
                if (gate == null || !gate.IsPositionInRange(position))
                    continue;

                float distance = (gate.transform.position - position).sqrMagnitude;
                if (distance >= bestDistance)
                    continue;

                bestDistance = distance;
                best = gate;
            }

            return best;
        }
    }
}
