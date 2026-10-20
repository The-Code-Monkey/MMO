using NUnit.Framework;
using Unity.AI.Navigation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace MMO.Tests
{
    /// <summary>
    /// Proves a baked zone registers its NavMesh with the engine. Click-to-move submits every
    /// click to <c>PlayerMover.RpcRequestMoveTo</c>, which rejects it when
    /// <c>NavMesh.SamplePosition</c> finds no mesh — the exact failure that left the destination
    /// marker standing while the player never moved. <c>ZoneSceneBakerTests</c> checks that the
    /// baker persisted the bake as an asset; only this test proves the loaded scene hands that
    /// data to the engine and the mesh covers the spawn area.
    /// </summary>
    public sealed class ZoneNavMeshPlayModeTests
    {
        private const string ZoneScenePath = "Assets/Scenes/Tutorial.unity";

        [Test]
        public void ZoneScene_LoadedNavMesh_AnswersSamplePositionNearSpawn()
        {
            Scene previous = SceneManager.GetActiveScene();
            Scene zone = EditorSceneManager.OpenScene(ZoneScenePath, OpenSceneMode.Single);

            try
            {
                NavMeshSurface surface = Object.FindFirstObjectByType<NavMeshSurface>();
                Assert.IsNotNull(surface,
                    "'Tutorial' has no NavMeshSurface, so every click-to-move destination would be rejected.");
                Assert.IsNotNull(surface.navMeshData,
                    "NavMeshSurface.navMeshData is null — the bake did not survive into the loaded scene.");

                int vertices = NavMesh.CalculateTriangulation().vertices.Length;
                Assert.Greater(vertices, 0,
                    "The zone NavMesh registered zero vertices — SamplePosition would fail everywhere.");

                bool sampled = NavMesh.SamplePosition(
                    new Vector3(0f, 0.05f, 0f), out NavMeshHit hit, 5f, NavMesh.AllAreas);
                Assert.IsTrue(sampled,
                    "No mesh within 5 m of the tutorial spawn — the server would reject every click there.");
                Debug.Log($"[ZoneNavMeshTest] sampled {hit.position} ({vertices} vertices).");
            }
            finally
            {
                if (!string.IsNullOrEmpty(previous.path))
                    EditorSceneManager.OpenScene(previous.path, OpenSceneMode.Single);
            }
        }
    }
}
