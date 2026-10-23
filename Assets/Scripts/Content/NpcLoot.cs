using System.Collections.Generic;
using FishNet.Object;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;
using Random = System.Random;

namespace MMO.Content
{
    /// <summary>
    /// Rolls a server-authored loot table when this NPC dies and awards it to the killer. The
    /// table is configuration on the prefab or set by the spawn controller; clients never see it.
    /// </summary>
    [RequireComponent(typeof(NPCStats))]
    [DisallowMultipleComponent]
    public sealed class NpcLoot : NetworkBehaviour
    {
        [SerializeField] private WeightedLootTable _table;

        private NPCStats _stats;
        private Random _rng = new();

        public WeightedLootTable Table => _table;

        /// <summary>Points the enemy at a table (a spawn controller or a test).</summary>
        public void SetTable(WeightedLootTable table) => _table = table;

        /// <summary>Replaces the roll source; tests use a deterministic random.</summary>
        public void SetRandom(Random rng) => _rng = rng ?? new Random();

        public override void OnStartNetwork()
        {
            if (!IsServer)
                return;

            _stats = GetComponent<NPCStats>();
            if (_stats != null)
                _stats.Died += OnDied;
        }

        public override void OnStopNetwork()
        {
            if (_stats != null)
                _stats.Died -= OnDied;
        }

        private void OnDied(PlayerInventory killer)
        {
            if (!IsServer || killer == null)
                return;

            if (_table != null)
            {
                List<LootDrop> drops = LootRules.Roll(_table, _rng);
                bool preferBag = ZoneRiskConfig.ResolveTierAt(transform.position) == ZoneRiskTier.Black;
                LootAwardResult result = LootAwarder.Award(
                    drops, killer, NetworkManager, transform.position, preferBag);

                if (result.Leftovers.Count > 0)
                {
                    Debug.LogError(
                        $"{nameof(NpcLoot)}: '{name}' could not place {result.Leftovers.Count} drop(s); they were not awarded.");
                }
            }

            if (killer.TryGetComponent(out IQuestKillSink quests))
                quests.NotifyKill(_stats != null ? _stats.TargetName : name);
        }
    }
}
