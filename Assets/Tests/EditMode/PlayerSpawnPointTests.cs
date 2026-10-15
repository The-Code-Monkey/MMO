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
        public void ChooseSpawn_NearestToSavedPosition_Wins()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(100f, 0f, 0f));

            Assert.AreEqual(b.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(95f, 0f, 0f)));
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
            Assert.AreEqual(Vector3.zero,
                PlayerSpawnPoint.ChooseSpawn(Array.Empty<PlayerSpawnPoint>(), new Vector3(3f, 0f, 3f)));
        }

        [Test]
        public void ChooseSpawn_NullPoints_ReturnsOrigin()
        {
            Assert.AreEqual(Vector3.zero, PlayerSpawnPoint.ChooseSpawn(null, new Vector3(3f, 0f, 3f)));
        }

        [Test]
        public void ChooseSpawn_SavedPositionOutsideTheZone_ClampsToTheNearestPoint()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero);
            PlayerSpawnPoint b = SpawnAt(new Vector3(20f, 0f, 0f));

            // A position from another (or a resized) zone must land the player at a real spawn
            // point rather than through the floor.
            Assert.AreEqual(b.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(1000f, 0f, 0f)));
        }

        [Test]
        public void ChooseSpawn_Equidistant_PrefersTheDefault()
        {
            PlayerSpawnPoint a = SpawnAt(Vector3.zero, isDefault: true);
            PlayerSpawnPoint b = SpawnAt(new Vector3(10f, 0f, 0f));

            Assert.AreEqual(a.transform.position,
                PlayerSpawnPoint.ChooseSpawn(new[] { a, b }, new Vector3(5f, 0f, 0f)));
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
                PlayerSpawnPoint.ChooseSpawn(new PlayerSpawnPoint[] { null, null }, new Vector3(4f, 0f, 0f)));
        }

        [Test]
        public void IsDefault_DefaultsToFalse()
        {
            PlayerSpawnPoint point = SpawnAt(Vector3.zero);
            Assert.IsFalse(point.IsDefault);
        }
    }
}
