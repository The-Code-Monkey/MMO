using System;
using System.Linq;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Gameplay;
using UnityEngine;

namespace MMO.Net
{
    /// <summary>
    /// Builds the damageable target dummy — a cube carrying a FishNet <see cref="NetworkObject"/>
    /// and a <see cref="TargetDummy"/>. It mirrors <see cref="PlayerPrefabFactory"/>: everything is
    /// built in code so a scene, a fresh process and a test all produce the same object.
    /// </summary>
    public static class TargetDummyFactory
    {
        public const string PrefabName = "TargetDummy";

        /// <summary>World width of the cube, in metres.</summary>
        public const float DefaultWidth = 1f;

        /// <summary>World height of the cube, in metres.</summary>
        public const float DefaultHeight = 2f;

        /// <summary>
        /// Transform scale that turns Unity's cube primitive (1 x 1 x 1) into
        /// <see cref="DefaultWidth"/> / <see cref="DefaultHeight"/>. Scaling scales collider and
        /// mesh together, so the BoxCollider a player clicks always matches what they see.
        /// </summary>
        public static Vector3 DefaultScale => new Vector3(DefaultWidth, DefaultHeight, DefaultWidth);

        /// <summary>
        /// Builds the dummy: a cube at the origin with a <see cref="NetworkObject"/> and the
        /// project's own networked <see cref="TargetDummy"/>.
        /// </summary>
        /// <remarks>
        /// The result is an ordinary active GameObject. Callers that keep it in a live scene should
        /// deactivate it — FishNet's pool activates the clone it spawns, so an inactive source is
        /// still spawnable.
        /// </remarks>
        public static NetworkObject Create()
        {
            // CreatePrimitive is what supplies the cube mesh, renderer and a BoxCollider whose
            // dimensions already match the mesh.
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            // NetworkObject before any NetworkBehaviour: behaviours resolve their owning
            // NetworkObject through it, and this is the order the FishNet editor tooling writes.
            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<TargetDummy>();

            PrimitiveAppearance.ApplyRenderPipelineMaterial(go);
            return networkObject;
        }

        /// <summary>
        /// True when <paramref name="dummyPrefab"/> is already one of the spawnables.
        /// </summary>
        /// <remarks>
        /// Only <see cref="SinglePrefabObjects"/> is searched: this project uses it exclusively,
        /// and the base <see cref="PrefabObjects"/> exposes no way to enumerate its contents.
        /// </remarks>
        public static bool Contains(PrefabObjects prefabs, NetworkObject dummyPrefab)
        {
            if (prefabs is not SinglePrefabObjects single || dummyPrefab == null)
                return false;

            return single.Prefabs.Contains(dummyPrefab);
        }

        /// <summary>
        /// Adds <paramref name="dummyPrefab"/> to the spawnable collection so the server and the
        /// client agree on a prefab id for it. FishNet derives prefab ids from list order, so both
        /// ends must register the prefabs in the same order.
        /// </summary>
        /// <returns>True when this call added the prefab, false when it was a no-op.</returns>
        /// <remarks>
        /// Unlike <see cref="PlayerPrefabFactory.Register"/>, which is called by
        /// <see cref="NetworkManagerFactory"/> before the manager's Awake runs, the dummy is
        /// normally registered at runtime — after <c>NetworkManager.Awake</c> has already handed
        /// out ids. <c>initializeAdded: true</c> therefore assigns the prefab id and caches the
        /// behaviours right here instead of waiting for a second Awake. While not playing this is
        /// a no-op and <c>NetworkManager.Awake</c> assigns the ids later, matching the player.
        /// </remarks>
        public static bool Register(PrefabObjects prefabs, NetworkObject dummyPrefab)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (dummyPrefab == null || Contains(prefabs, dummyPrefab))
                return false;

            prefabs.AddObject(dummyPrefab, checkForDuplicates: true, initializeAdded: true);
            return true;
        }
    }
}