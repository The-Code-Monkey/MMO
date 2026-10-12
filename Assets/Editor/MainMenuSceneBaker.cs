using MMO.Bootstrap;
using MMO.Net;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>
    /// Materialises the Main Menu scene (<c>Assets/Scenes/MainMenu.unity</c>): an empty scene with
    /// a token-authenticated NetworkManager (via the network scene baker, so the shared factory
    /// builds manager, prefabs, gate and persistence exactly as every other scene) plus a
    /// <see cref="MainMenuBootstrap"/> wired to it. The UI itself is <em>not</em> baked — it is
    /// rebuilt by the bootstrap from <see cref="MMO.UI.MainMenuFactory"/> whenever the scene runs,
    /// keeping the project's no-committed-scene rule.
    ///
    /// <para>
    /// Creating the scene also adds it to the editor build settings, so the main menu actually ships
    /// as the first scene a player boots into.
    /// </para>
    /// </summary>
    public static class MainMenuSceneBaker
    {
        public const string MenuPath = "Tools/MMO/Create Main Menu Scene";
        public const string ScenePath = "Assets/Scenes/MainMenu.unity";

        [MenuItem(MenuPath)]
        public static void CreateMainMenuScene()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // This baked NetworkManager re-wires its own token validator from the serialized
            // UseTokenAuth flag at runtime (see TokenAuthBootstrap), so a scene saved here is
            // authenticated wherever it runs.
            var settings = new MMOConnectionSettings(
                MMOConnectionSettings.DefaultPort,
                MMOConnectionSettings.DefaultMaxClients,
                MMOConnectionSettings.DefaultHandshakeTimeoutSeconds,
                useTokenAuth: true);

            NetworkManagerSceneBaker.CreateInActiveScene(settings);

            MainMenuBootstrap bootstrap = CreateBootstrap();
            Undo.RegisterCreatedObjectUndo(bootstrap.gameObject, "Create Main Menu Bootstrap");

            string savedScene = SaveScene(scene);
            EditorBuildSettingsAsset(savedScene);

            Selection.activeGameObject = bootstrap.gameObject;
            EditorGUIUtility.PingObject(bootstrap.gameObject);
        }

        private static MainMenuBootstrap CreateBootstrap()
        {
            var bootstrapObject = new GameObject("MainMenuBootstrap");
            MainMenuBootstrap bootstrap = bootstrapObject.AddComponent<MainMenuBootstrap>();
            bootstrap.SetDefaultZone("127.0.0.1", MMOConnectionSettings.DefaultPort);
            bootstrap.SetNetworkManager(
                Object.FindFirstObjectByType<FishNet.Managing.NetworkManager>());
            return bootstrap;
        }

        private static string SaveScene(Scene scene)
        {
            if (!Directory.Exists("Assets/Scenes"))
                Directory.CreateDirectory("Assets/Scenes");

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Could not save the Main Menu scene.");
                return string.Empty;
            }

            return ScenePath;
        }

        /// <summary>Adds the scene to the build list (enabled) unless it is already there.</summary>
        private static void EditorBuildSettingsAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            foreach (EditorBuildSettingsScene existing in scenes)
            {
                if (existing.path == path)
                    return;
            }

            var list = new List<EditorBuildSettingsScene>(scenes)
            {
                new EditorBuildSettingsScene(path, true),
            };
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}