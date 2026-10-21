using System.Collections;
using MMO.Net;
using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TestTools;

namespace MMO.Tests
{
    /// <summary>
    /// Proves the hole a crafting station carves in the NavMesh actually bends a route around
    /// it. The server walks the route the NavMesh hands it, so a station that does not carve
    /// leaves the route running straight through the station — the player walks through the
    /// workbench.
    ///
    /// <para>
    /// The area is built here rather than opened: a runtime-baked <see cref="NavMeshSurface"/>
    /// over a test floor, parked far from the project zones so no other mesh can answer the
    /// queries. The station is the real prefab's body and obstacle settings.
    /// </para>
    /// </summary>
    public sealed class CraftingStationNavMeshTests
    {
        /// <summary>Where the test area sits — far enough from the zones that their mesh cannot answer.</summary>
        private const float AreaCentre = 500f;

        private const float HalfFootprint = 1f;

        private GameObject _root;
        private GameObject _station;
        private NavMeshSurface _surface;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // Carving only happens while the game runs, so this suite is a play-mode suite; when an
            // editor runs the play-mode assembly without entering play mode there is nothing to
            // tear down in the running world.
            if (!Application.isPlaying)
            {
                Object.DestroyImmediate(_root);
                Object.DestroyImmediate(_surface != null ? _surface.gameObject : null);
                yield break;
            }

            if (_root != null)
                Object.Destroy(_root);

            if (_surface != null)
                Object.Destroy(_surface.gameObject);

            yield return null;
        }

        [UnityTest]
        public IEnumerator CarvedStation_MakesTheRouteBendAroundIt()
        {
            if (!Application.isPlaying)
                Assert.Ignore("NavMesh carving only runs in play mode — run this suite in PlayMode mode.");

            BuildArea();
            BuildStation();

            // The carve lands on the next navmesh update, not on the frame the obstacle appears.
            yield return WaitForCarve();

            Vector3 centre = _station.transform.position;
            centre.y = 0.05f;

            bool walkableUnderStation = NavMesh.SamplePosition(centre, out _, 0.5f, NavMesh.AllAreas);
            Assert.IsFalse(walkableUnderStation,
                "The station carved no hole: the NavMesh still has walkable ground under it, so a route runs straight through.");

            Vector3 start = centre + new Vector3(-4f, 0f, 0f);
            Vector3 end = centre + new Vector3(4f, 0f, 0f);

            var path = new NavMeshPath();
            bool found = NavMesh.CalculatePath(start, end, NavMesh.AllAreas, path);

            Assert.IsTrue(found, "No route across the test area at all — the NavMesh did not bake.");
            Assert.AreEqual(NavMeshPathStatus.PathComplete, path.status,
                "The route across the area must be complete; a partial route means the mesh is broken.");
            Assert.Greater(path.corners.Length, 2,
                "The route ran straight through the station: a carved hole must add corners to bend around it.");

            for (int i = 1; i < path.corners.Length; i++)
            {
                Assert.IsFalse(CrossesStation(path.corners[i - 1], path.corners[i], centre),
                    "The route passes through the station's footprint: the carve is not bending the path around it.");
            }
        }

        private void BuildArea()
        {
            _root = new GameObject("CraftingStationNavMeshTestArea");
            _root.transform.position = new Vector3(AreaCentre, 0f, AreaCentre);

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            floor.transform.SetParent(_root.transform, false);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);

            GameObject surfaceGo = new GameObject("NavMeshSurface");
            surfaceGo.transform.SetParent(_root.transform, false);
            _surface = surfaceGo.AddComponent<NavMeshSurface>();
            _surface.collectObjects = CollectObjects.All;
            _surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
            _surface.layerMask = ~0;
            _surface.BuildNavMesh();
        }

        private void BuildStation()
        {
            _station = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _station.name = "CraftingStation";
            _station.transform.SetParent(_root.transform, false);
            _station.transform.localPosition = new Vector3(0f, CraftingStationPrefabFactory.DefaultHeight / 2f, 0f);
            _station.transform.localScale = CraftingStationPrefabFactory.DefaultScale;

            NavMeshObstacle obstacle = _station.AddComponent<NavMeshObstacle>();
            obstacle.size = CraftingStationPrefabFactory.ObstacleLocalSize;
            obstacle.center = Vector3.zero;
            obstacle.carving = true;
        }

        private IEnumerator WaitForCarve()
        {
            Vector3 centre = _station.transform.position;
            centre.y = 0.05f;

            for (int i = 0; i < 120; i++)
            {
                if (!NavMesh.SamplePosition(centre, out _, 0.5f, NavMesh.AllAreas))
                    yield break;

                yield return null;
            }
        }

        /// <summary>True when the straight leg from <paramref name="a"/> to <paramref name="b"/> runs through the station.</summary>
        private static bool CrossesStation(Vector3 a, Vector3 b, Vector3 centre)
        {
            float length = Vector3.Distance(
                new Vector3(a.x, 0f, a.z),
                new Vector3(b.x, 0f, b.z));

            int steps = Mathf.Max(2, Mathf.CeilToInt(length / 0.05f));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 point = Vector3.Lerp(a, b, (float)i / steps);
                if (Mathf.Abs(point.x - centre.x) < HalfFootprint &&
                    Mathf.Abs(point.z - centre.z) < HalfFootprint)
                    return true;
            }

            return false;
        }
    }
}
