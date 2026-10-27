using System.Reflection;
using FishNet.Object;
using UnityEngine;

namespace MMO.Tests
{
    /// <summary>
    /// Builds a <see cref="NetworkBehaviour"/> that reports itself as a spawned, networked,
    /// server-initialized object, so FishNet's compile-time <c>[Server]</c> guards run in
    /// EditMode without a live NetworkManager. FishNet only fills <c>_networkObjectCache</c> when
    /// it spawns the object; without it every guarded method dereferences null and throws.
    /// Test-only: production objects get this through FishNet.
    /// </summary>
    internal static class NetworkBehaviourTestFactory
    {
        private static readonly FieldInfo NetworkObjectCache =
            typeof(NetworkBehaviour).GetField("_networkObjectCache", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly MethodInfo IsServerInitializedSetter =
            typeof(NetworkObject).GetProperty(nameof(NetworkObject.IsServerInitialized)).GetSetMethod(true);

        public static T Attach<T>(GameObject go) where T : NetworkBehaviour
        {
            NetworkObject networkObject = go.GetComponent<NetworkObject>();
            if (networkObject == null)
                networkObject = go.AddComponent<NetworkObject>();

            networkObject.SetIsNetworked(true);
            IsServerInitializedSetter.Invoke(networkObject, new object[] { true });

            T behaviour = go.AddComponent<T>();
            NetworkObjectCache.SetValue(behaviour, networkObject);
            return behaviour;
        }

        public static T Create<T>(string name = null) where T : NetworkBehaviour
            => Attach<T>(new GameObject(name ?? typeof(T).Name));
    }
}
