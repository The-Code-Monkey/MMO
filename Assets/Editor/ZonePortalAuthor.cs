using MMO.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Authors a zone's portal trigger.</summary>
    public static class ZonePortalAuthor
    {
        public static void Create(
            Transform zoneRoot,
            string portalName,
            string sourceServerId,
            string targetHost,
            ushort targetPort,
            string targetServerId,
            string targetZoneName)
        {
            var go = ZoneSceneScaffold.CreateChild(portalName, zoneRoot);
            go.transform.localPosition = new Vector3(
                0f, ZoneSceneBaker.PortalHeight / 2f, ZoneSceneBaker.GroundExtent / 2f - 0.5f);

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
    }
}
