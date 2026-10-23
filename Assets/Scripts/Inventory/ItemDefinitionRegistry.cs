using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Inventory
{
    /// <summary>
    /// Resolves an <see cref="ItemDefinition.ItemName"/> key back to the definition both ends of
    /// the connection agree on.
    ///
    /// The wire only ever carries the key — a <c>ScriptableObject</c> reference cannot be
    /// serialized — so something has to turn that key into the local asset. Two paths do:
    /// definitions under a <c>Resources</c> folder load on demand (the zero-wiring default, and
    /// what the shipped items rely on), and anything else registers itself, either from
    /// <c>ItemDefinition.OnEnable</c> when the asset is loaded or explicitly from a bootstrap
    /// or a test.
    ///
    /// Keys are the definition's <see cref="ItemDefinition.ItemName"/>, which must therefore be
    /// unique within the project; registering a second definition under a key already in use
    /// replaces the first and says so, because a silently shadowed item is a bug that only shows
    /// up in the game.
    /// </summary>
    public static class ItemDefinitionRegistry
    {
        /// <summary>
        /// The <c>Resources</c> subfolder the shipped item assets live under — the item above is
        /// <c>Resources/Items/Iron_Ore.asset</c>.
        /// </summary>
        public const string ItemsFolder = "Items";

        private static readonly Dictionary<string, ItemDefinition> Registered = new();

        /// <summary>How many definitions are currently registered. Exposed for diagnostics and tests.</summary>
        public static int Count => Registered.Count;

        /// <summary>
        /// Puts <paramref name="definition"/> under its own <see cref="ItemDefinition.ItemName"/>.
        /// Called automatically when a definition asset is loaded; tests and bootstraps that
        /// build definitions in code call it after giving the definition a name.
        /// </summary>
        /// <exception cref="ArgumentNullException">The definition is null.</exception>
        /// <exception cref="ArgumentException">The definition has no name yet.</exception>
        public static void Register(ItemDefinition definition)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));

            if (string.IsNullOrEmpty(definition.ItemName))
                throw new ArgumentException(
                    $"{nameof(ItemDefinitionRegistry)}: {nameof(definition)} has no {nameof(ItemDefinition.ItemName)} to register under.",
                    nameof(definition));

            // Re-registering the same asset (a reload, or a bootstrap running twice) is a no-op;
            // only a different instance taking over a key is worth reporting.
            if (Registered.TryGetValue(definition.ItemName, out ItemDefinition existing) && existing != definition)
            {
                // The conditional is parenthesized because ':' inside a C# 9 interpolated string
                // is read as a format specifier and fails to parse without them.
                Debug.LogWarning(
                    $"{nameof(ItemDefinitionRegistry)}: '{definition.ItemName}' is already registered as " +
                    $"'{(existing != null ? existing.name : "<destroyed>")}'; replacing it with '{definition.name}'.");
            }

            Registered[definition.ItemName] = definition;
        }

        /// <summary>
        /// Removes <paramref name="definition"/> if it is still the definition its key points
        /// at, so a stale asset cannot evict its own replacement.
        /// </summary>
        public static void Unregister(ItemDefinition definition)
        {
            if (definition == null || string.IsNullOrEmpty(definition.ItemName))
                return;

            if (Registered.TryGetValue(definition.ItemName, out ItemDefinition existing) && existing == definition)
                Registered.Remove(definition.ItemName);
        }

        /// <summary>
        /// Finds the definition for <paramref name="itemName"/>, falling back to
        /// <c>Resources.Load</c> so assets under a Resources folder need no explicit wiring.
        /// Returns null for an empty key or an item that exists nowhere, which the caller
        /// treats as "refuse the action" rather than an error — a missing item is data the
        /// server must reject, not a crash.
        /// </summary>
        public static ItemDefinition Resolve(string itemName)
        {
            if (string.IsNullOrEmpty(itemName))
                return null;

            if (Registered.TryGetValue(itemName, out ItemDefinition definition) && definition != null)
                return definition;

            // Either never registered or registered and then destroyed; Unity's null overload
            // above tells those apart from a real miss.
            ItemDefinition loaded = Resources.Load<ItemDefinition>(itemName);
            if (loaded != null)
                Registered[itemName] = loaded;

            return loaded;
        }

        /// <summary>
        /// Forgets every registration. The shipped items still resolve afterwards through the
        /// <c>Resources</c> fallback, which is what keeps test ordering irrelevant.
        /// </summary>
        public static void Clear() => Registered.Clear();

        /// <summary>
        /// Loads every <see cref="ItemDefinition"/> asset under <c>Resources/Items</c> and
        /// registers the whole set at once.
        ///
        /// <para>
        /// Each asset already registers itself on load, so this only forces the load to happen.
        /// It matters because a shipped item is only ever named on the wire — a tool's kind and
        /// tier live on its definition, and the tool gate resolves them by name — so an item
        /// asset nobody loads is an item the server cannot answer questions about. Safe to call
        /// repeatedly: re-registering the same instance just overwrites the dictionary entry with
        /// itself.
        /// </para>
        /// </summary>
        public static void LoadAllFromResources()
        {
            foreach (ItemDefinition definition in Resources.LoadAll<ItemDefinition>(ItemsFolder))
                Register(definition);
        }
    }
}
