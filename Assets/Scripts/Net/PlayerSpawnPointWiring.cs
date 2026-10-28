using FishNet.Component.Spawning;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>Points FishNet's PlayerSpawner at a zone's authored spawn points.</summary>
    public static class PlayerSpawnPointWiring
    {
        public static void Wire(PlayerSpawner spawner, PlayerSpawnPoint[] points)
        {
            if (spawner == null || points == null || points.Length == 0)
                return;

            var transforms = new Transform[points.Length];
            for (int i = 0; i < points.Length; i++)
                transforms[i] = points[i].transform;

            spawner.Spawns = transforms;
        }
    }
}
