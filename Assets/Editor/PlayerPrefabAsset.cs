using System.IO;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Asset path/load/delete/folder operations for the player prefab.</summary>
    public static class PlayerPrefabAsset
    {
        public static NetworkObject Load()
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabBaker.PrefabAssetPath);
            return asset != null ? asset.GetComponent<NetworkObject>() : null;
        }

        public static bool Delete()
            => AssetDatabase.DeleteAsset(PlayerPrefabBaker.PrefabAssetPath);

        public static void EnsureFolder()
        {
            if (AssetDatabase.IsValidFolder(PlayerPrefabBaker.PrefabFolder))
                return;

            string parent = Path.GetDirectoryName(PlayerPrefabBaker.PrefabFolder)?.Replace('\\', '/');
            string leaf = Path.GetFileName(PlayerPrefabBaker.PrefabFolder);

            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                return;

            if (!AssetDatabase.IsValidFolder(parent))
                AssetDatabase.CreateFolder("Assets", leaf);
            else
                AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
