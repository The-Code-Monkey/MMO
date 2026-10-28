using System;
using FishNet.Component.Spawning;
using FishNet.Managing;
using FishNet.Object;

namespace MMO.Net
{
    /// <summary>Installs FishNet's <see cref="PlayerSpawner"/> on an already-awoken NetworkManager.</summary>
    public static class PlayerSpawnerInstaller
    {
        /// <summary>
        /// Puts <see cref="PlayerSpawner"/> on the manager's own GameObject and points it at
        /// <paramref name="playerPrefab"/>, which is how FishNet 4.x is told to spawn a prefab
        /// whenever a player connects.
        /// </summary>
        /// <remarks>
        /// The spawner must be on the NetworkManager object or a child of it. It is added only
        /// after <c>NetworkManager.Awake</c> has run, because it subscribes to
        /// <c>SceneManager.OnClientLoadedStartScenes</c> during its own Awake.
        /// </remarks>
        public static PlayerSpawner Attach(NetworkManager manager, NetworkObject playerPrefab)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            PlayerSpawner spawner = manager.GetComponent<PlayerSpawner>();
            if (spawner == null)
                spawner = manager.gameObject.AddComponent<PlayerSpawner>();

            if (playerPrefab != null)
                spawner.SetPlayerPrefab(playerPrefab);

            return spawner;
        }
    }
}
