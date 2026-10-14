using System;
using System.Collections.Generic;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// How dangerous a zone is. The ordinal order matters: <see cref="RiskRules.IsValidTier"/>
    /// and the death table both range-check against it, and a saved or wire-sent value is
    /// interpreted by its number, so new tiers must be appended rather than inserted.
    /// </summary>
    public enum ZoneRiskTier
    {
        /// <summary>No permanent loss; death only dents gear, repaired with gold.</summary>
        Safe = 0,

        /// <summary>No permanent loss, but a heavier repair bill.</summary>
        Yellow = 1,

        /// <summary>Permanent loss of progress (respawn), no loot drop.</summary>
        Red = 2,

        /// <summary>Full loot: inventory and worn gear drop and the profile is wiped.</summary>
        Black = 3,
    }

    /// <summary>
    /// What death does to a player in a given zone. A plain read-only value: the server resolves
    /// one from the zone's tier and then applies it, and a test can assert the whole outcome with
    /// one equality check instead of four field reads.
    /// </summary>
    public readonly struct DeathOutcome
    {
        /// <summary>True when the death cannot be shrugged off — the player respawns, losing progress.</summary>
        public bool IsPermanent { get; }

        /// <summary>True when the bag and worn gear are dropped as loot and wiped from the profile.</summary>
        public bool DropInventory { get; }

        /// <summary>Durability points subtracted from each destructible worn item.</summary>
        public int DurabilityDamage { get; }

        /// <summary>Gold the survivor must sink to repair the damage; never more than they hold.</summary>
        public int RepairCost { get; }

        public DeathOutcome(bool isPermanent, bool dropInventory, int durabilityDamage, int repairCost)
        {
            IsPermanent = isPermanent;
            DropInventory = dropInventory;
            DurabilityDamage = Mathf.Max(0, durabilityDamage);
            RepairCost = Mathf.Max(0, repairCost);
        }
    }

    /// <summary>
    /// The death and zone-risk decision surface as pure functions: what dying costs, how much
    /// durability a hit removes, and how much of a wallet a repair can actually take.
    ///
    /// Every method is static, side-effect free and takes its inputs as arguments for the same
    /// reason as <see cref="GatheringRules"/>: an EditMode test can resolve a black-zone death in
    /// microseconds, without a server, a scene or a spawned player. The two networked halves —
    /// <see cref="ZoneRiskConfig"/> and <see cref="PlayerDeath"/> — own none of this arithmetic;
    /// they read the zone's tier, call <see cref="ResolveDeath"/> and only then mutate
    /// replicated state.
    ///
    /// An unknown tier resolves to <see cref="ZoneRiskTier.Safe"/> rather than the enum's
    /// <c>default</c>: a misauthored zone must fail toward the least destructive behaviour, so a
    /// typo can never turn a hub into a full-loot arena. The same applies to a value cast from an
    /// out-of-range integer.
    /// </summary>
    public static class RiskRules
    {
        /// <summary>Durability a death removes in a <see cref="ZoneRiskTier.Safe"/> zone.</summary>
        public const int SafeDurabilityDamage = 5;

        /// <summary>Gold a death costs to repair in a <see cref="ZoneRiskTier.Safe"/> zone.</summary>
        public const int SafeRepairCost = 50;

        /// <summary>Durability a death removes in a <see cref="ZoneRiskTier.Yellow"/> zone.</summary>
        public const int YellowDurabilityDamage = 10;

        /// <summary>Gold a death costs to repair in a <see cref="ZoneRiskTier.Yellow"/> zone.</summary>
        public const int YellowRepairCost = 100;

        /// <summary>Durability a death removes in a <see cref="ZoneRiskTier.Red"/> zone.</summary>
        public const int RedDurabilityDamage = 20;

        /// <summary>True when <paramref name="tier"/> is one of the declared tiers.</summary>
        public static bool IsValidTier(ZoneRiskTier tier)
        {
            return tier >= ZoneRiskTier.Safe && tier <= ZoneRiskTier.Black;
        }

        /// <summary>
        /// The outcome of dying in a zone of <paramref name="tier"/>. An undeclared tier is
        /// treated as <see cref="ZoneRiskTier.Safe"/>.
        /// </summary>
        public static DeathOutcome ResolveDeath(ZoneRiskTier tier)
        {
            switch (tier)
            {
                case ZoneRiskTier.Safe:
                    return new DeathOutcome(isPermanent: false, dropInventory: false, SafeDurabilityDamage, SafeRepairCost);
                case ZoneRiskTier.Yellow:
                    return new DeathOutcome(isPermanent: false, dropInventory: false, YellowDurabilityDamage, YellowRepairCost);
                case ZoneRiskTier.Red:
                    return new DeathOutcome(isPermanent: true, dropInventory: false, RedDurabilityDamage, 0);
                case ZoneRiskTier.Black:
                    return new DeathOutcome(isPermanent: true, dropInventory: true, 0, 0);
                default:
                    return ResolveDeath(ZoneRiskTier.Safe);
            }
        }

        /// <summary>
        /// The outcome for a tier that arrived as a raw integer (a save, an inspector value, a
        /// stale asset). Anything outside the declared range is clamped to
        /// <see cref="ZoneRiskTier.Safe"/>.
        /// </summary>
        public static DeathOutcome ResolveDeath(int tier)
        {
            return ResolveDeath(IsValidTier((ZoneRiskTier)tier) ? (ZoneRiskTier)tier : ZoneRiskTier.Safe);
        }

        /// <summary>
        /// Durability left after <paramref name="damage"/> is applied: never below zero, and a
        /// negative input reads as no damage rather than as "about to add durability".
        /// </summary>
        public static int WearDurability(int durability, int damage)
        {
            return Math.Max(0, durability - Math.Max(0, damage));
        }

        /// <summary>
        /// How much gold a repair may actually take: never more than the wallet holds, and a
        /// non-positive balance pays nothing. This is the clamp that lets the server deduct the
        /// bill in one step without ever driving a balance negative.
        /// </summary>
        public static long ClampRepair(long currency, int repairCost)
        {
            if (currency <= 0)
                return 0;

            long cost = Math.Max(0, repairCost);
            return Math.Min(currency, cost);
        }

        /// <summary>
        /// Wears one item instance by <paramref name="damage"/>, returning the updated instance.
        /// An empty instance is returned untouched; an indestructible item (a definition with no
        /// durability, or one that no longer resolves) is left as-is; and an item whose current
        /// durability is non-positive is read from its definition's maximum rather than treated as
        /// already broken, so the first repair-less death is not a free pass.
        /// </summary>
        public static ItemInstance Wear(ItemInstance item, int damage)
        {
            if (item.IsEmpty)
                return item;

            ItemDefinition definition = ItemDefinitionRegistry.Resolve(item.ItemName);
            int max = definition != null ? definition.MaxDurability : 0;
            if (max <= 0 || damage <= 0)
                return item;

            int current = item.Durability <= 0 ? max : item.Durability;
            item.Durability = WearDurability(current, damage);
            return item;
        }
    }
}
