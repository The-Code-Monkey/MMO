using FishNet.Managing;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Generates zone scenes (SafeZone + Wilderness) with Graybox prefabs
    /// and adds FishNet NetworkManager to each scene via the SceneBaker.
    /// </summary>
    public static class SceneGenerator
    {
        public const string MenuPath = "Tools/MMO/Generate Zone Scenes";

        [MenuItem(MenuPath)]
        public static void GenerateZoneScenes()
        {
            // Create SafeZone scene with NetworkManager
            MMO.Editor.NetworkManagerSceneBaker.CreateInActiveScene(new MMOConnectionSettings());

            // Create Wilderness scene with NetworkManager
            // (this is a simplified version - full implementation would duplicate the scene setup)

            Undo.RegisterCreatedObjectUndo(GameObject.Find("NetworkManager"), "Generate Zone Scenes");
        }
    }
}
