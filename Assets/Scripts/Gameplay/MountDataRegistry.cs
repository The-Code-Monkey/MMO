using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Resolves a mount item name back to the <see cref="MountData"/> both ends agree on, the same
    /// way <c>ItemDefinitionRegistry</c> resolves items: the wire only carries the key, and assets
    /// under <c>Resources/Mounts</c> load on demand.
    ///
    /// Keys are <see cref="MountData.ItemName"/>, so the key must be unique within the project and
    /// an asset that ships under Resources must have a file name matching that key for the
    /// <c>Resources.Load</c> fallback to find it.
    /// </summary>
    public static class MountDataRegistry
    {
        /// <summary>The <c>Resources</c> subfolder shipped mount assets live under.</summary>
        public const string MountsFolder = "Mounts";

        private static readonly Dictionary<string, MountData> Registered = new();

        /// <summary>How many mounts are registered. Exposed for diagnostics and tests.</summary>
        public static int Count => Registered.Count;

        /// <exception cref="ArgumentNullException">The data is null.</exception>
        /// <exception cref="ArgumentException">The data has no item name yet.</exception>
        public static void Register(MountData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (string.IsNullOrEmpty(data.ItemName))
                throw new ArgumentException(
                    $"{nameof(MountDataRegistry)}: {nameof(MountData)} has no {nameof(MountData.ItemName)} to register under.",
                    nameof(data));

            Registered[data.ItemName] = data;
        }

        /// <summary>Removes the data only while it is still what its key points at.</summary>
        public static void Unregister(MountData data)
        {
            if (data == null || string.IsNullOrEmpty(data.ItemName))
                return;

            if (Registered.TryGetValue(data.ItemName, out MountData existing) && existing == data)
                Registered.Remove(data.ItemName);
        }

        /// <summary>
        /// The mount for <paramref name="itemName"/>, falling back to <c>Resources.Load</c>.
        /// Returns null for an empty key or an unknown mount; the caller refuses the action.
        /// </summary>
        public static MountData Resolve(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (Registered.TryGetValue(itemName, out MountData data) && data != null)
                return data;

            MountData loaded = Resources.Load<MountData>(itemName);
            if (loaded == null)
            {
                // Shipped mounts live under Resources/Mounts, so the plain key only resolves
                // assets that sit at the Resources root. Clients rely on this fallback because
                // they do not run the server's LoadAllFromResources bootstrap.
                loaded = Resources.Load<MountData>($"{MountsFolder}/{itemName}");
            }

            if (loaded != null)
                Registered[itemName] = loaded;

            return loaded;
        }

        /// <summary>Forgets every registration; shipped assets still resolve through Resources.</summary>
        public static void Clear() => Registered.Clear();

        /// <summary>Loads every mount asset under <c>Resources/Mounts</c> and registers it.</summary>
        public static void LoadAllFromResources()
        {
            foreach (MountData data in Resources.LoadAll<MountData>(MountsFolder))
                Register(data);
        }
    }
}
