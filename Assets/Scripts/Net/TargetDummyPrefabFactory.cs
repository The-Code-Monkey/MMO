using System;
using System.Linq;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace MMO.Net
{
    /// <summary>
    /// Builds the target dummy — a red capsule carrying a FishNet <see cref="NetworkObject"/> and
    /// the project's own networked <see cref="NPCStats"/> — and wires it into a
    /// <see cref="NetworkManager"/> so the server spawns one when it starts.
    ///
    /// The dummy is a <em>server-spawned</em> entity: nobody instantiates it in a scene. A
    /// <see cref="TargetDummySpawner"/> on the NetworkManager object instantiates the prefab and
    /// calls <c>ServerManager.Spawn</c> when the server starts, so every client sees the dummy in
    /// the exact same state because it only ever exists through the server's spawn path.
    ///
    /// Everything is built in code so a scene, a fresh process and a test all produce the same
    /// object. The editor baker (<c>MMO.Editor.TargetDummyBaker</c>) saves what <see cref="Create"/>
    /// returns to disk as a prefab asset.
    /// </summary>
    public static class TargetDummyPrefabFactory
    {
        public const string PrefabName = "TargetDummy";

        /// <summary>World height of the capsule, in metres.</summary>
        public const float DefaultHeight = 2f;

        /// <summary>World radius of the capsule, in metres.</summary>
        public const float DefaultRadius = 0.5f;

        /// <summary>Enemy colour. Red is how players tell an actor apart at a glance.</summary>
        public static readonly Color RedColor = Color.red;

        /// <summary>
        /// Transform scale that turns Unity's capsule primitive (0.5 radius, 2 tall) into
        /// <see cref="DefaultRadius"/> / <see cref="DefaultHeight"/>. The primitive's
        /// CapsuleCollider is authored in local units, so scaling scales collider and mesh together
        /// and they cannot drift apart.
        /// </summary>
        public static Vector3 DefaultScale => new(
            DefaultRadius * 2f,
            DefaultHeight / 2f,
            DefaultRadius * 2f);

        /// <summary>
        /// Where the dummy appears when the server spawns it: a few metres in front of the origin,
        /// standing on the ground. The capsule is centred (0, height/2) so its base sits at y = 0.
        /// </summary>
        public static Vector3 DefaultSpawnPosition => new(0f, DefaultHeight / 2f, 5f);

        /// <summary>
        /// Builds the target dummy: a red capsule at the origin with a <see cref="NetworkObject"/>
        /// and the project's own networked <see cref="NPCStats"/>.
        /// </summary>
        /// <remarks>
        /// The result is an ordinary active GameObject, so it can be saved as a prefab asset
        /// (<c>PrefabUtility.SaveAsPrefabAsset</c>) or registered as a spawnable. Callers that keep
        /// it in a live scene should deactivate it — the spawner activates the clone and FishNet's
        /// pool activates what it spawns, so an inactive source is still perfectly spawnable.
        /// </remarks>
        public static NetworkObject Create()
        {
            // CreatePrimitive is what supplies the capsule mesh, renderer and a CapsuleCollider
            // whose dimensions already match the mesh.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            // NetworkObject before any NetworkBehaviour: behaviours resolve their owning
            // NetworkObject through it, and this is the order the FishNet editor tooling writes.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<NPCStats>();

            ApplyRedMaterial(go);
            return networkObject;
        }

        /// <summary>
        /// True when <paramref name="targetDummy"/> is already one of the spawnables.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SinglePrefabObjects"/> is searched: this project uses it exclusively,
        /// and the base <see cref="PrefabObjects"/> exposes no way to enumerate its contents.
        /// </remarks>
        public static bool Contains(PrefabObjects prefabs, NetworkObject targetDummy)
        {
            if (prefabs is not SinglePrefabObjects single || targetDummy == null)
                return false;

            return single.Prefabs.Contains(targetDummy);
        }

        /// <summary>
        /// Adds <paramref name="targetDummy"/> to the spawnable collection so the server and the
        /// client agree on a prefab id for it. FishNet derives prefab ids from list order, so the
        /// player prefab must be registered first and the dummy second, on every end.
        /// </summary>
        /// <returns>True when this call added the prefab, false when it was a no-op.</returns>
        public static bool Register(PrefabObjects prefabs, NetworkObject targetDummy, bool initializeAdded = false)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (targetDummy == null || Contains(prefabs, targetDummy))
                return false;

            // initializeAdded: false leaves prefab id assignment to NetworkManager.Awake, which is
            // the only place that knows the final list order and whether the game is playing.
            // Callers registering a prefab after Awake (at runtime) pass true instead.
            prefabs.AddObject(targetDummy, checkForDuplicates: true, initializeAdded: initializeAdded);
            return true;
        }

        /// <summary>
        /// Puts the <see cref="TargetDummySpawner"/> on the NetworkManager's own GameObject and
        /// points it at <paramref name="targetDummy"/>, which is how a server-spawned entity is told
        /// to instantiate and spawn the dummy when the server starts.
        /// </summary>
        public static TargetDummySpawner Attach(NetworkManager manager, NetworkObject targetDummy)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            TargetDummySpawner spawner = manager.GetComponent<TargetDummySpawner>();
            if (spawner == null)
                spawner = manager.gameObject.AddComponent<TargetDummySpawner>();

            if (targetDummy != null)
                spawner.SetDummyPrefab(targetDummy);

            return spawner;
        }

        /// <summary>
        /// Swaps the primitive's built-in material for a red one using the active render pipeline's
        /// shader. Without this a dummy in a URP project would render magenta, and an uncoloured
        /// capsule would not read as an enemy.
        /// </summary>
        private static void ApplyRedMaterial(GameObject go)
        {
            Shader shader = ResolveShader();
            if (shader == null)
                return;

            // A fresh material, not a tweak of a shared asset: per-prefab and leak-free.
            Material material = new(shader);
            material.name = "TargetDummyRed";

            // URP Lit uses _BaseColor; the built-in pipeline uses _Color. Set whichever exists.
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", RedColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", RedColor);

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = material;
        }

        private static Shader ResolveShader()
        {
            RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline != null && pipeline.defaultMaterial != null)
                return pipeline.defaultMaterial.shader;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            return shader != null ? shader : Shader.Find("Standard");
        }
    }
}