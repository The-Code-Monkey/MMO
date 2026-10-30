using MMO.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Authors a zone's portal triggers, one per connected zone.</summary>
    public static class ZonePortalAuthor
    {
        /// <summary>How far inside the boundary the trigger centre sits.</summary>
        private const float EdgeInset = 0.5f;

        public static void Create(
            Transform zoneRoot,
            string portalName,
            string sourceServerId,
            string targetHost,
            ushort targetPort,
            string targetServerId,
            string targetZoneName,
            float groundExtent = ZoneSceneBaker.GroundExtent,
            int slotIndex = 0,
            int slotCount = 1)
        {
            var go = ZoneSceneScaffold.CreateChild(portalName, zoneRoot);
            go.transform.localPosition = PositionFor(slotIndex, slotCount, groundExtent);

            var collider = go.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(
                ZoneSceneBaker.GroundTile * 2f, ZoneSceneBaker.PortalHeight, ZoneSceneBaker.GroundTile);

            var trigger = go.AddComponent<ZoneTransferTrigger>();
            var serialized = new SerializedObject(trigger);
            serialized.FindProperty("_targetHost").stringValue = targetHost;
            serialized.FindProperty("_targetPort").intValue = targetPort;
            serialized.FindProperty("_targetServerId").stringValue = targetServerId;
            serialized.FindProperty("_targetZoneName").stringValue = targetZoneName;
            serialized.FindProperty("_sourceServerId").stringValue = sourceServerId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Places the trigger just inside the boundary. Slot zero is the north edge; further
        /// slots are distributed clockwise around the perimeter, so any number of links fits.
        /// </summary>
        public static Vector3 PositionFor(int slotIndex, int slotCount, float groundExtent)
        {
            Vector3 point = EdgePointFor(slotIndex, slotCount, groundExtent);

            if (Mathf.Abs(point.z) > Mathf.Abs(point.x))
                point.z -= Mathf.Sign(point.z) * EdgeInset;
            else
                point.x -= Mathf.Sign(point.x) * EdgeInset;

            point.y = ZoneSceneBaker.PortalHeight / 2f;
            return point;
        }

        /// <summary>The un-inset point on the boundary that a portal slot occupies.</summary>
        internal static Vector3 EdgePointFor(int slotIndex, int slotCount, float groundExtent)
        {
            slotCount = Mathf.Max(1, slotCount);
            float half = groundExtent / 2f;
            float distance = Mathf.Repeat(slotIndex * (4f * groundExtent) / slotCount, 4f * groundExtent);

            if (distance < half)
                return new Vector3(distance, 0f, half);

            distance -= half;
            if (distance < groundExtent)
                return new Vector3(half, 0f, half - distance);

            distance -= groundExtent;
            if (distance < groundExtent)
                return new Vector3(half - distance, 0f, -half);

            distance -= groundExtent;
            if (distance < groundExtent)
                return new Vector3(-half, 0f, -half + distance);

            distance -= groundExtent;
            return new Vector3(-half + distance, 0f, half);
        }
    }
}
