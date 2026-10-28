using FishNet.Component.Spawning;
using FishNet.Managing;
using MMO.Net;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Authors a zone's player spawn points and wires FishNet's spawner.</summary>
    public static class ZoneSpawnAuthor
    {
        public static GameObject[] CreateSpawnPoints(Transform zoneRoot, int count)
        {
            Transform parent = ZoneSceneScaffold.CreateChild(ZoneSceneBaker.SpawnPointsName, zoneRoot).transform;

            // Y is 1.0f so a 2-unit tall capsule with a centre pivot rests on top of the floor.
            Vector3[] positions =
            {
                new Vector3(0f, 1f, 0f),
                new Vector3(6f, 1f, -6f),
                new Vector3(-6f, 1f, -6f),
                new Vector3(0f, 1f, 6f),
                new Vector3(8f, 1f, 8f),
                new Vector3(-8f, 1f, 8f),
            };

            count = Mathf.Clamp(count, 1, positions.Length);

            var spawns = new GameObject[count];
            for (int i = 0; i < count; i++)
            {
                var spawn = new GameObject($"PlayerSpawn_{i + 1}");
                spawn.transform.SetParent(parent, false);
                spawn.transform.localPosition = positions[i];

                var point = spawn.AddComponent<PlayerSpawnPoint>();
                point.SetDefault(i == 0);
                spawns[i] = spawn;
            }

            return spawns;
        }

        public static void Wire(NetworkManager manager, GameObject[] spawns)
        {
            var spawner = manager.GetComponent<PlayerSpawner>();
            if (spawner == null)
                return;

            var transforms = new Transform[spawns.Length];
            for (int i = 0; i < spawns.Length; i++)
                transforms[i] = spawns[i].transform;

            spawner.Spawns = transforms;
        }
    }
}
