using System;
using System.Linq;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace MMO.Net
{
    /// <summary>
    /// Builds the resource node — an ore-coloured cube carrying a FishNet
    /// <see cref="NetworkObject"/> and the project's own networked <see cref="ResourceNode"/> —
    /// so an iron vein and an oak tree are the same networked object wearing different
    /// configuration. It mirrors <see cref="TargetDummyPrefabFactory"/>: everything is built in
    /// code so a scene, a fresh process and a test all produce the same object, and the editor
    /// baker (<c>MMO.Editor.ResourceNodeBaker</c>) saves what <see cref="Create"/> returns to
    /// disk as a prefab asset.
    ///
    /// Unlike the target dummy there is no spawner: a node is placed by a level or instantiated
    /// by whoever owns the resource, and FishNet only needs it in the spawnable collection for
    /// that runtime instantiation — which is what <see cref="Register"/> is for.
    /// </summary>
    public static class ResourceNodePrefabFactory
    {
        public const string PrefabName = "ResourceNode";

        /// <summary>World width of the vein, in metres.</summary>
        public const float DefaultWidth = 2f;

        /// <summary>World height of the vein, in metres.</summary>
        public const float DefaultHeight = 1.2f;

        /// <summary>World depth of the vein, in metres.</summary>
        public const float DefaultDepth = 2f;

        /// <summary>
        /// The node's colour. A grey-brown rock is how a player reads "ore vein" at a glance
        /// against the red of an enemy and the default grey of the environment.
        /// </summary>
        public static readonly Color OreColor = new(0.45f, 0.4f, 0.35f);

        /// <summary>
        /// Transform scale that turns Unity's cube primitive (1 x 1 x 1) into a squat
        /// <see cref="DefaultWidth"/> x <see cref="DefaultHeight"/> x <see cref="DefaultDepth"/>
        /// slab. Scaling scales collider and mesh together, so the BoxCollider a player clicks
        /// always matches what they see.
        /// </summary>
        public static Vector3 DefaultScale => new(DefaultWidth, DefaultHeight, DefaultDepth);

        /// <summary>
        /// Builds the node: a squat cube with a <see cref="NetworkObject"/> and the project's own
        /// networked <see cref="ResourceNode"/>, wearing the shipped default resource
        /// definition.
        /// </summary>
        /// <remarks>
        /// The result is an ordinary active GameObject, so it can be saved as a prefab asset
        /// (<c>PrefabUtility.SaveAsPrefabAsset</c>) or registered as a spawnable. Callers that
        /// keep it in a live scene should deactivate it — FishNet's pool activates the clone it
        /// spawns, so an inactive source is still perfectly spawnable.
        /// </remarks>
        public static NetworkObject Create()
        {
            return Create(ResourceDefinition.LoadDefault());
        }

        /// <summary>
        /// Builds the node wearing <paramref name="definition"/> — the seam a test or a
        /// content tool uses to bake a different kind of resource into a node. A null
        /// definition leaves the node inert rather than broken: it yields nothing and says so.
        /// </summary>
        public static NetworkObject Create(ResourceDefinition definition)
        {
            // CreatePrimitive is what supplies the cube mesh, renderer and a BoxCollider whose
            // dimensions already match the mesh.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            // NetworkObject before any NetworkBehaviour: behaviours resolve their owning
            // NetworkObject through it, and this is the order the FishNet editor tooling writes.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            ResourceNode node = go.AddComponent<ResourceNode>();
            node.SetDefinition(definition);

            ApplyOreMaterial(go);
            return networkObject;
        }

        /// <summary>
        /// True when <paramref name="nodePrefab"/> is already one of the spawnables.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SinglePrefabObjects"/> is searched: this project uses it exclusively,
        /// and the base <see cref="PrefabObjects"/> exposes no way to enumerate its contents.
        /// </remarks>
        public static bool Contains(PrefabObjects prefabs, NetworkObject nodePrefab)
        {
            if (prefabs is not SinglePrefabObjects single || nodePrefab == null)
                return false;

            return single.Prefabs.Contains(nodePrefab);
        }

        /// <summary>
        /// Adds <paramref name="nodePrefab"/> to the spawnable collection so the server and the
        /// client agree on a prefab id for it. FishNet derives prefab ids from list order, so
        /// both ends must register the prefabs in the same order: the player is always first
        /// (id 0), the target dummy second (id 1), and a node registers after them — whoever
        /// instantiates a node registers it, and only after the manager has taken the two
        /// permanent occupants.
        /// </summary>
        /// <returns>True when this call added the prefab, false when it was a no-op.</returns>
        /// <remarks>
        /// Like <see cref="TargetDummyFactory.Register"/> — the runtime registration path — this
        /// passes <c>initializeAdded: true</c>: a node is instantiated after
        /// <c>NetworkManager.Awake</c> has already handed out ids, so the id and the cached
        /// behaviours are assigned right here instead of waiting for a second Awake. While not
        /// playing this is a no-op and <c>NetworkManager.Awake</c> assigns the ids later.
        /// </remarks>
        public static bool Register(PrefabObjects prefabs, NetworkObject nodePrefab)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (nodePrefab == null || Contains(prefabs, nodePrefab))
                return false;

            prefabs.AddObject(nodePrefab, checkForDuplicates: true, initializeAdded: true);
            return true;
        }

        /// <summary>
        /// Puts the node's colour on the primitive using the active render pipeline's shader.
        /// Without this a node in a URP project would render magenta, and an uncoloured cube
        /// would not read as a vein.
        /// </summary>
        private static void ApplyOreMaterial(GameObject go)
        {
            Shader shader = ResolveShader();
            if (shader == null)
                return;

            // A fresh material, not a tweak of a shared asset: per-prefab and leak-free.
            Material material = new(shader);
            material.name = "ResourceNodeOre";

            // URP Lit uses _BaseColor; the built-in pipeline uses _Color. Set whichever exists.
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", OreColor);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", OreColor);

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
