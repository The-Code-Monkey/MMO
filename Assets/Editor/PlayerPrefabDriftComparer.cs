using System.Text;
using FishNet.Object;
using MMO.Client;
using MMO.Net;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>
    /// Compares a saved player prefab with the runtime factory's component order, and requires the
    /// baked cosmetic visual to be present.
    /// </summary>
    public static class PlayerPrefabDriftComparer
    {
        public static bool MatchesFactory(NetworkObject saved)
        {
            // Prefabs baked before the character visual existed are structurally correct but
            // invisible; force one rebuild so the model is attached.
            Transform visual = saved != null
                ? saved.transform.Find(PlayerPrefabAuthoring.VisualRootName)
                : null;
            if (visual == null || visual.GetComponentInChildren<PlayerCharacterVisual>(true) == null)
                return false;

            NetworkObject built = PlayerPrefabFactory.Create();
            try
            {
                return Describe(built) == Describe(saved);
            }
            finally
            {
                Object.DestroyImmediate(built.gameObject);
            }
        }

        private static string Describe(NetworkObject player)
        {
            var builder = new StringBuilder();
            foreach (NetworkBehaviour behaviour in player.GetComponents<NetworkBehaviour>())
            {
                if (builder.Length > 0)
                    builder.Append(',');

                builder.Append(behaviour.GetType().FullName);
            }

            return builder.ToString();
        }
    }
}
