using System.IO;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Net;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Saves <see cref="BankTellerPrefabFactory.Create"/> to disk and wires the prefab into a
    /// scene's spawnable collection. Mirrors the crafting-station baker: the teller a generated
    /// scene spawns and the teller a fresh process builds are the same object.
    /// </summary>
    public static class BankTellerBaker
    {
        public const string MenuPath = "Tools/MMO/Create Bank Teller Prefab";
        public const string PrefabFolder = "Assets/Prefabs";
        public const string PrefabAssetPath = PrefabFolder + "/" + BankTellerPrefabFactory.PrefabName + ".prefab";

        public static string AbsolutePrefabPath => Path.Combine(
            Path.GetDirectoryName(Application.dataPath) ?? ".",
            PrefabAssetPath);

        [MenuItem(MenuPath)]
        public static void BakeBankTellerPrefabMenu()
        {
            NetworkObject prefab = EnsureBankTellerPrefab();
            if (prefab == null)
                return;

            Selection.activeObject = prefab.gameObject;
            EditorGUIUtility.PingObject(prefab.gameObject);
        }

        public static NetworkObject EnsureBankTellerPrefab()
        {
            NetworkObject existing = LoadBankTellerPrefab();
            if (existing != null)
                return existing;

            EnsureFolder();

            NetworkObject built = BankTellerPrefabFactory.Create();
            try
            {
                GameObject asset = PrefabUtility.SaveAsPrefabAsset(built.gameObject, PrefabAssetPath);
                if (asset == null)
                {
                    Debug.LogError(
                        $"Could not save the bank teller prefab to '{PrefabAssetPath}'. " +
                        "A server would have nothing to spawn a teller from.");
                    return null;
                }

                AssetDatabase.SaveAssets();
                return asset.GetComponent<NetworkObject>();
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        public static NetworkObject WireBankTeller(NetworkManager manager)
        {
            if (manager == null)
                return null;

            NetworkObject teller = EnsureBankTellerPrefab();
            if (teller == null)
                return null;

            if (manager.SpawnablePrefabs == null)
            {
                var prefabs = ScriptableObject.CreateInstance<SinglePrefabObjects>();
                prefabs.name = NetworkManagerFactory.SpawnablePrefabsName;
                manager.SpawnablePrefabs = prefabs;
            }

            BankTellerPrefabFactory.Register(manager.SpawnablePrefabs, teller);
            return teller;
        }

        /// <summary>The saved prefab's NetworkObject, or null when it has not been baked yet.</summary>
        public static NetworkObject LoadBankTellerPrefab()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabAssetPath);
            return asset != null ? asset.GetComponent<NetworkObject>() : null;
        }

        public static bool DeleteBankTellerPrefab()
            => AssetDatabase.DeleteAsset(PrefabAssetPath);

        private static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(PrefabFolder))
                return;

            string parent = Path.GetDirectoryName(PrefabFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(PrefabFolder);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;

            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
