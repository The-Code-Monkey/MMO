using FishNet.Managing;
using FishNet.Transporting.Tugboat;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Net;
using MMO.Social;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Scene-level NetworkManager identity and transport tweaks for a zone.</summary>
    public static class ZoneNetworkAuthoring
    {
        public static void RemoveFixedSpawners(NetworkManager manager)
        {
            foreach (TargetDummySpawner dummy in manager.GetComponents<TargetDummySpawner>())
                Object.DestroyImmediate(dummy);

            foreach (CraftingStationSpawner station in manager.GetComponents<CraftingStationSpawner>())
                Object.DestroyImmediate(station);
        }

        public static void EnableTransportReuseAddress(NetworkManager manager)
        {
            var tugboat = manager.GetComponent<Tugboat>();
            if (tugboat == null)
                return;

            var serialized = new SerializedObject(tugboat);
            serialized.FindProperty("_reuseAddress").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void ConfigureZoneIdentity(
            NetworkManager manager, string serverId, string templateId, ushort port)
        {
            SocialHubService hub = manager.GetComponent<SocialHubService>();
            if (hub == null)
                return;

            hub.ZoneId = serverId;

            var serialized = new SerializedObject(hub);
            serialized.FindProperty("_instanceHostAddress").stringValue = ZoneSceneBaker.DefaultTargetHost;
            serialized.FindProperty("_instanceHostPort").intValue = port;
            serialized.FindProperty("_zoneTemplateId").stringValue = templateId;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void CreateSun()
        {
            var go = new GameObject("Directional Light");
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            go.transform.position = new Vector3(0f, 3f, 0f);

            Light light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = new Color(1f, 0.956f, 0.839f);
            light.shadows = LightShadows.Soft;
        }
    }
}
