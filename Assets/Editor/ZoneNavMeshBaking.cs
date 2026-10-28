using System;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Bakes a zone NavMesh and persists its data beside the scene.</summary>
    public static class ZoneNavMeshBaking
    {
        public static NavMeshSurface Bake(Transform zoneRoot)
        {
            var go = new GameObject("NavMeshSurface");
            go.transform.SetParent(zoneRoot, false);
            var surface = go.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();
            return surface;
        }

        /// <summary>
        /// Persists the in-memory NavMeshData as an asset next to the scene, deleting any prior
        /// bake so re-baking does not accumulate orphans.
        /// </summary>
        public static void SaveAsset(NavMeshSurface surface, string scenePath)
        {
            if (surface == null || surface.navMeshData == null)
                throw new InvalidOperationException(
                    $"NavMeshSurface on '{surface?.name}' has no baked data to save.");

            string sceneDir = Path.GetDirectoryName(scenePath);
            string sceneName = Path.GetFileNameWithoutExtension(scenePath);
            string assetDir = Path.Combine(sceneDir ?? "Assets", sceneName);
            if (!Directory.Exists(assetDir))
                Directory.CreateDirectory(assetDir);

            string assetPath = Path.Combine(assetDir, "NavMesh-" + surface.name + ".asset");

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);

            AssetDatabase.CreateAsset(surface.navMeshData, assetPath);
            AssetDatabase.SaveAssets();
        }
    }
}
