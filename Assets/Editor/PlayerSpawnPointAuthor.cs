using System.Collections.Generic;
using MMO.Net;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MMO.Editor
{
    /// <summary>Ensures every PlayerSpawn_* object carries a PlayerSpawnPoint marker.</summary>
    public static class PlayerSpawnPointAuthor
    {
        public static int EnsureComponents(Scene scene, string spawnPointsName)
        {
            int count = 0;
            bool hasDefault = false;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform spawnRoot in FindChildrenNamed(root.transform, spawnPointsName))
                {
                    foreach (Transform child in spawnRoot)
                    {
                        if (child == null)
                            continue;

                        PlayerSpawnPoint point = child.GetComponent<PlayerSpawnPoint>();
                        if (point == null)
                            point = child.gameObject.AddComponent<PlayerSpawnPoint>();

                        count++;
                        if (point.IsDefault)
                            hasDefault = true;
                    }
                }
            }

            if (count > 0 && !hasDefault)
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform spawnRoot in FindChildrenNamed(root.transform, spawnPointsName))
                    {
                        if (spawnRoot.childCount > 0)
                        {
                            spawnRoot.GetChild(0).GetComponent<PlayerSpawnPoint>()?.SetDefault(true);
                            break;
                        }
                    }
                }
            }

            return count;
        }

        private static IEnumerable<Transform> FindChildrenNamed(Transform root, string name)
        {
            if (root == null)
                yield break;

            if (root.name == name)
                yield return root;

            for (int i = 0; i < root.childCount; i++)
            {
                foreach (Transform found in FindChildrenNamed(root.GetChild(i), name))
                    yield return found;
            }
        }
    }
}
