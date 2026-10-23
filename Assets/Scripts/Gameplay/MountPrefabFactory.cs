using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Builds the single generic mount body a <see cref="MountData"/> describes: a capsule scaled
    /// by <see cref="MountData.Size"/> and, when the data names one, the cosmetic
    /// <see cref="MountData.ModelPrefab"/> dropped over it.
    ///
    /// This is what makes a mount data-only. Authoring a new horse or gryphon is "set isFlying,
    /// speed and size, point at a model" — not "build another prefab". The result is purely
    /// cosmetic: colliders are stripped because the player's own mover and capsule remain the
    /// only physics surface, so a model can never block the player or carve the NavMesh.
    /// </summary>
    public static class MountPrefabFactory
    {
        /// <summary>
        /// Builds the visual for <paramref name="data"/>, or null when the data is null. The
        /// caller parents and destroys it; nothing here registers a prefab or touches FishNet.
        /// </summary>
        public static GameObject Create(MountData data)
        {
            if (data == null)
                return null;

            var root = new GameObject(string.IsNullOrEmpty(data.MountName) ? "Mount" : data.MountName);
            root.transform.localScale = Vector3.one * data.Size;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            StripColliders(body.transform);

            if (data.ModelPrefab != null)
            {
                // The model is the mount's visible shape; the generic capsule is only a fallback.
                body.GetComponent<MeshRenderer>().enabled = false;

                GameObject model = Object.Instantiate(data.ModelPrefab, root.transform, false);
                model.name = "Model";
                StripColliders(model.transform);
            }

            return root;
        }

        private static void StripColliders(Transform root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (Application.isPlaying)
                    Object.Destroy(collider);
                else
                    Object.DestroyImmediate(collider);
            }
        }
    }
}
