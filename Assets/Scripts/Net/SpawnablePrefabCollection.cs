using System;
using System.Collections.Generic;
using System.Linq;
using FishNet.Managing.Object;
using FishNet.Object;

namespace MMO.Net
{
    /// <summary>
    /// Canonical registration of spawnable prefabs. FishNet derives prefab ids from list order,
    /// so both ends must add the same prefabs in the same order; this class owns that invariant.
    /// </summary>
    public static class SpawnablePrefabCollection
    {
        /// <summary>
        /// True when <paramref name="playerPrefab"/> is already one of the spawnables.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SinglePrefabObjects"/> is searched: this project uses it exclusively,
        /// and the base <see cref="PrefabObjects"/> exposes no way to enumerate its contents.
        /// </remarks>
        public static bool Contains(PrefabObjects prefabs, NetworkObject playerPrefab)
        {
            if (prefabs is not SinglePrefabObjects single || playerPrefab == null)
                return false;

            return single.Prefabs.Contains(playerPrefab);
        }

        /// <summary>
        /// Adds <paramref name="playerPrefab"/> to the spawnable collection so the server and the
        /// client agree on a prefab id for it.
        /// </summary>
        /// <remarks>
        /// A collection serialized by an earlier bake can hold null or destroyed entries, or can
        /// have the player somewhere other than index 0. Either desyncs the ids the two ends hand
        /// out, so <see cref="SinglePrefabObjects"/> is rebuilt in place: the player first, then
        /// the surviving prefabs in their original relative order.
        /// </remarks>
        /// <returns>True when this call added or re-ordered the prefab, false when it was a no-op.</returns>
        public static bool Register(PrefabObjects prefabs, NetworkObject playerPrefab)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (playerPrefab == null)
                return false;

            if (prefabs is SinglePrefabObjects single && NeedsRewire(single, playerPrefab))
            {
                Rewire(single, playerPrefab);
                return true;
            }

            if (Contains(prefabs, playerPrefab))
                return false;

            // initializeAdded: false leaves prefab id assignment to NetworkManager.Awake, which is
            // the only place that knows the final list order and whether the game is playing.
            prefabs.AddObject(playerPrefab, checkForDuplicates: true, initializeAdded: false);
            return true;
        }

        private static bool NeedsRewire(SinglePrefabObjects prefabs, NetworkObject playerPrefab)
        {
            IReadOnlyList<NetworkObject> list = prefabs.Prefabs;

            if (list.Count == 0)
                return false;

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                    return true;
            }

            return list[0] != playerPrefab;
        }

        private static void Rewire(SinglePrefabObjects prefabs, NetworkObject playerPrefab)
        {
            var survivors = new List<NetworkObject>();
            foreach (NetworkObject existing in prefabs.Prefabs)
            {
                if (existing != null && existing != playerPrefab)
                    survivors.Add(existing);
            }

            prefabs.Clear();

            // checkForDuplicates: false — the list was just cleared, so nothing can collide.
            prefabs.AddObject(playerPrefab, checkForDuplicates: false, initializeAdded: false);
            if (survivors.Count > 0)
                prefabs.AddObjects(survivors, checkForDuplicates: false, initializeAdded: false);
        }
    }
}
