using MMO.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    public static class NetworkManagerSceneBaker
    {
        [MenuItem("Tools/MMO/Create NetworkManager In Scene")]
        public static void CreateNetworkManagerInScene()
        {
            Scene active = SceneManager.GetActiveScene();

            foreach (GameObject root in active.GetRootGameObjects())
            {
                FishNet.Managing.NetworkManager existing =
                    root.GetComponentInChildren<FishNet.Managing.NetworkManager>(true);
                if (existing != null)
                {
                    Selection.activeGameObject = existing.gameObject;
                    EditorGUIUtility.PingObject(existing);
                    return;
                }
            }

            FishNet.Managing.NetworkManager manager = NetworkManagerFactory.Create();
            Undo.RegisterCreatedObjectUndo(manager.gameObject, "Create NetworkManager");
            EditorSceneManager.MarkSceneDirty(active);
            Selection.activeGameObject = manager.gameObject;
        }
    }
}
