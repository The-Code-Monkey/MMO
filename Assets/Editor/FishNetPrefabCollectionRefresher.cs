using System.Linq;
using System.Reflection;
using FishNet.Object;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Runs FishNet's own "Refresh Default Prefabs" generator after a baker creates or overwrites
    /// NetworkObject prefabs. FishNet stores an AssetPathHash on each prefab and uses it to identify
    /// spawnables; a freshly saved prefab starts at 0, so without this refresh every rebuilt
    /// resource-node variant collides at hash 0 and the build logs an error per pair.
    ///
    /// The generator is internal to FishNet's runtime assembly, so this calls its public static
    /// entry point by reflection rather than copying FishNet's hash algorithm.
    /// </summary>
    public static class FishNetPrefabCollectionRefresher
    {
        public static bool Refresh()
        {
            MethodInfo method = typeof(NetworkObject).Assembly
                .GetType("FishNet.Editing.PrefabCollectionGenerator.Generator")
                ?.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(candidate =>
                    candidate.Name == "GenerateFull" &&
                    candidate.GetParameters().Length == 3);

            if (method == null)
            {
                Debug.LogError(
                    "Could not find FishNet's prefab collection generator. Refresh the default prefabs " +
                    "from Tools > Fish-Networking > Utility > Refresh Default Prefabs.");
                return false;
            }

            method.Invoke(null, new object[] { null, true, true });
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
