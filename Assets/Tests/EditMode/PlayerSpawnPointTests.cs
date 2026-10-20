using System;
using System.Collections.Generic;
using MMO.Net;
using NUnit.Framework;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// The pure spawn-selection rule behind <see cref="WorldBootstrap"/>: a joining player is
    /// placed at the spawn point nearest their saved position, the zone's default when there is no
    /// saved position, and the origin — never a null-reference — when the zone has no points at all.
    /// No clock, no network: this is arithmetic over transforms.
    /// </summary>
    public sealed class PlayerSpawnPointTests
    {
        private readonly List<GameObject> _objects = new();

        [TearDown]
        public void Cleanup()
        {
            foreach (GameObject go in _objects)
            {
                if (go != null)
                    UnityEngine.Object.DestroyImmediate(go);
            }

            _objects.Clear();
        }

        private PlayerSpawnPoint SpawnAt(Vector3 position, bool isDefault = false)
        {
            var go = new GameObject("SpawnPoint");
            go.transform.position = position;
            _objects.Add(go);

            var point = go.AddComponent<PlayerSpawnPoint>();
            point.SetDefault(isDefault);
            return point;
        }

        [Test]
        public void ChooseSpawn_WalkableSavedPosition_IsRestoredVerbatim()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(100f, 0f, 0f));

            // A relog must put the player back where they logged out, not snap to a marker.
            var saved = new Vector3(3f, 0f, 4f);
            Assert.AreEqual(saved, PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, saved));
        }

        [Test]
        public void ChooseSpawn_UnwalkableSavedPosition_ClampsToTheNearestPoint()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(20f, 0f, 0f));

            // A saved position off the mesh (another map, a resized zone) must land the player at a
            // real spawn point rather than leave them stranded off the NavMesh.
            Assert.AreEqual(b.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(1000f, 0f, 0f), _ => false));
        }

        [Test]
        public void ChooseSpawn_NoSavedPosition_UsesDefault()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(50f, 0f, 0f), isDefault: true);

            Assert.AreEqual(b.transform.position, PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, null));
        }

        [Test]
        public void ChooseSpawn_NoDefault_PointsFallBackToTheFirst()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(50f, 0f, 0f));

            Assert.AreEqual(a.transform.position, PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, null));
        }

        [Test]
        public void ChooseSpawn_NoPoints_ReturnsOrigin()
        {
            Assert.AreEqual(Vector3.zero, PlayerSpawnPoint.ChooseSpawn(Array.Empty<PlayerSpawnPoint>(), null));
        }

        [Test]
        public void ChooseSpawn_NoPoints_SavedPositionStillRestored()
        {
            // A relog does not depend on authored markers: a walkable save stands even when the
            // zone has no spawn points at all.
            Assert.AreEqual(new Vector3(3f, 0f, 3f),
                PlayerSpawnPoint.ChooseSpawn(Array.Empty<PlayerSpawnPoint>(), new Vector3(3f, 0f, 3f), _ => true));
        }

        [Test]
        public void ChooseSpawn_NullPoints_ReturnsOrigin()
        {
            Assert.AreEqual(Vector3.zero,
                PlayerSpawnPoint.ChooseSpawn(null, new Vector3(3f, 0f, 3f), _ => false));
        }

        [Test]
        public void ChooseSpawn_WalkableCheckStatic_ClampsWhenRejected()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(10f, 0f, 0f));

            // The walkable predicate is the engine boundary: a rejected save falls back to the
            // nearest point even when a nearby point exists.
            Assert.AreEqual(a.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(1f, 0f, 0f), _ => false));
        }

        [Test]
        public void ChooseSpawn_EquidistantRejected_PrefersTheDefault()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero, isDefault: true);
            PlayerSpawnPoint b = SpawnAt(new Vector3(10f, 0f, 0f));

            Assert.AreEqual(a.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(5f, 0f, 0f), _ => false));
        }

        [Test]
        public void ChooseSpawn_IgnoresNullEntries()
        {
            PlayerSpawnPoint a = SpawnAt(new Vector3(4f, 0f, 0f));

            Assert.AreEqual(a.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new PlayerSpawnPoint[] { null, a }, new Vector3(4f, 0f, 0f)));
        }

        [Test]
        public void ChooseSpawn_AllEntriesNull_ReturnsOrigin()
        {
            Assert.AreEqual(Vector3.zero,
                PlayerSpawnPoint.ChooseSpawn(new PlayerSpawnPoint[] { null, null }, new Vector3(4f, 0f, 0f), _ => false));
        }

        [Test]
        public void IsDefault_DefaultsToFalse()
        {
            PlayerSpawnPoint point = SpawnAt(Vector3.zero);
            Assert.IsFalse(point.IsDefault);
        }
    }
}
