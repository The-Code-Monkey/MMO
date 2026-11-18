using System;
using System.Linq;
using FishNet.Managing.Object;
using FishNet.Object;
using MMO.Banking;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace MMO.Net
{
    /// <summary>
    /// Builds the generic bank teller: a capsule ("pill") placeholder standing in for a humanoid,
    /// carrying a <see cref="NetworkObject"/> and a <see cref="BankTeller"/>. The same prefab is
    /// spawned for a personal teller and a guild teller; the marker configures which bank it opens.
    ///
    /// Everything is built in code so a scene, a fresh process and a test produce the same object.
    /// The editor baker (<c>MMO.Editor.BankTellerBaker</c>) saves what <see cref="Create"/> returns
    /// to disk as a prefab asset.
    /// </summary>
    public static class BankTellerPrefabFactory
    {
        public const string PrefabName = "BankTeller";

        /// <summary>World height of the capsule, in metres.</summary>
        public const float DefaultHeight = 1.8f;

        /// <summary>World radius of the capsule, in metres.</summary>
        public const float DefaultRadius = 0.4f;

        /// <summary>Teller colour: a calm blue so a teller reads apart from a red enemy.</summary>
        public static readonly Color BankerColor = new Color(0.2f, 0.35f, 0.75f, 1f);

        /// <summary>Transform scale that turns Unity's capsule primitive into the bank teller body.</summary>
        public static Vector3 DefaultScale => new(
            DefaultRadius * 2f,
            DefaultHeight / 2f,
            DefaultRadius * 2f);

        public static NetworkObject Create()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = PrefabName;
            go.transform.localScale = DefaultScale;

            NetworkObject networkObject = go.AddComponent<NetworkObject>();
            go.AddComponent<BankTeller>();

            // The pointer's raycast needs a non-trigger collider, and the player should not walk through the
            // teller.
            if (go.TryGetComponent(out CapsuleCollider collider))
                collider.isTrigger = false;

            var obstacle = go.AddComponent<NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = NavMeshObstacleShape.Capsule;
            obstacle.center = Vector3.zero;
            obstacle.radius = DefaultRadius;
            obstacle.height = DefaultHeight;

            ApplyBankerMaterial(go);
            return networkObject;
        }

        public static bool Contains(PrefabObjects prefabs, NetworkObject teller)
        {
            if (prefabs is not SinglePrefabObjects single || teller == null)
                return false;

            return single.Prefabs.Contains(teller);
        }

        public static bool Register(PrefabObjects prefabs, NetworkObject teller, bool initializeAdded = false)
        {
            if (prefabs == null)
                throw new ArgumentNullException(nameof(prefabs));

            if (teller == null || Contains(prefabs, teller))
                return false;

            prefabs.AddObject(teller, checkForDuplicates: true, initializeAdded: initializeAdded);
            return true;
        }

        private static void ApplyBankerMaterial(GameObject go)
        {
            Shader shader = ResolveShader();
            if (shader == null)
            {
                PrimitiveAppearance.ApplyRenderPipelineMaterial(go);
                return;
            }

            Material material = new(shader);
            material.name = "BankTellerBlue";
            material.color = BankerColor;
            material.SetColor("_BaseColor", BankerColor);
            material.SetColor("_Color", BankerColor);

            if (go.TryGetComponent(out Renderer renderer))
                renderer.material = material;
        }

        private static Shader ResolveShader()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader != null)
                return shader;

            shader = Shader.Find("Standard");
            if (shader != null)
                return shader;

            return Shader.Find("Unlit/Color");
        }
    }
}
