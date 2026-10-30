using FishNet.Component.Transforming;
using FishNet.Object;
using MMO.Client;
using MMO.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Writes the non-obvious serialized player-prefab fields.</summary>
    public static class PlayerPrefabAuthoring
    {
        public const string VisualRootName = "Visuals";

        // The rigged model's origin is at its feet, while the player root is the capsule centre.
        // Half the 2 m capsule puts the contact point at -1; the sole sits 0.1 below the model
        // origin, so the visual drops 0.9 to stand the feet on the capsule bottom.
        private const float VisualFootDrop = -0.9f;

        public static void EquipDefaultWeapon(NetworkObject player)
        {
            PlayerCombat combat = player != null ? player.GetComponent<PlayerCombat>() : null;
            if (combat == null)
                return;

            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(
                PlayerPrefabBaker.DefaultWeaponAssetPath);
            if (weapon == null)
            {
                Debug.LogWarning(
                    $"No weapon found at '{PlayerPrefabBaker.DefaultWeaponAssetPath}' — the player will spawn unarmed.");
                return;
            }

            var inspector = new SerializedObject(combat);
            inspector.FindProperty("_equippedWeapon").objectReferenceValue = weapon;
            inspector.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// Nests the rigged cosmetic model under the player root and hides the primitive capsule
        /// renderer. The capsule keeps its mesh and collider so existing prefab checks and the
        /// server's movement volume are unchanged.
        /// </summary>
        public static void AttachCharacterVisual(NetworkObject player)
        {
            if (player == null)
                return;

            PlayerCharacterAssetBaker.PlayerCharacterAssets assets =
                PlayerCharacterAssetBaker.EnsureAssets();
            if (assets?.VisualPrefab == null)
                return;

            Transform existing = player.transform.Find(VisualRootName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var visual = (GameObject)PrefabUtility.InstantiatePrefab(assets.VisualPrefab);
            visual.name = VisualRootName;
            visual.transform.SetParent(player.transform, false);
            visual.transform.localPosition = new Vector3(0f, VisualFootDrop, 0f);
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;

            MeshRenderer capsule = player.GetComponent<MeshRenderer>();
            if (capsule != null)
                capsule.enabled = false;
        }

        public static void MakeNetworkTransformServerAuthoritative(NetworkObject player)
        {
            if (player == null)
                return;

            NetworkTransform netTransform = player.GetComponent<NetworkTransform>();
            if (netTransform == null)
                return;

            var serialized = new SerializedObject(netTransform);
            serialized.FindProperty("_clientAuthoritative").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
