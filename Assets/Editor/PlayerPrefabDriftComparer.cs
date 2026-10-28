using System.Text;
using FishNet.Object;
using MMO.Net;
using UnityEngine;

namespace MMO.Editor
{
    /// <summary>Compares a saved player prefab with the runtime factory's component order.</summary>
    public static class PlayerPrefabDriftComparer
    {
        public static bool MatchesFactory(NetworkObject saved)
        {
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
