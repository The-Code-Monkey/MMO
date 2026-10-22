using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A harvestable node — an iron ore vein, an oak tree — that exists in the world only
    /// through the server. Two <c>SyncVar</c>s carry its whole replicated state:
    /// <see cref="Remaining"/>, the durability (how many harvests are left before it is
    /// empty), and <see cref="Depleted"/>, whether it is currently empty and waiting out its
    /// respawn timer. Every client holds a copy of both and is refused if it tries to write
    /// them, so a "vein" can never be re-filled by the machine clicking it.
    ///
    /// <para>
    /// What a node <em>is</em> — ore or lumber, five harvests or one, a pickaxe or bare hands,
    /// a T2 vein or a T1 one — lives on the <see cref="ResourceDefinition"/> asset the node
    /// carries, the same way <see cref="MMO.Crafting.CraftingStation"/> wears a
    /// <see cref="MMO.Crafting.RecipeData"/>. One component, one prefab, every kind of
    /// resource: the balance moves into an asset a designer edits, and the tool rule is a kind
    /// and a tier instead of a string somebody has to spell the same way twice.
    /// </para>
    ///
    /// <para>
    /// The definition is configuration, not state: both ends read the same prefab, exactly like
    /// <see cref="WeaponData"/> on <see cref="PlayerCombat"/>, so nothing here is replicated
    /// and nothing here has a setter.
    /// </para>
    ///
    /// <para>
    /// The harvest itself is a round trip: a click on this node's collider routes through
    /// <see cref="RequestGatherFromLocalPlayer"/> into <see cref="PlayerGathering"/>, whose
    /// owner-checked <c>[ServerRpc]</c> delivers the intent, and the server comes back here to
    /// <see cref="TryGather"/> once per tick. This component never asks a client for anything —
    /// it is the thing being validated against, not the validator.
    /// </para>
    ///
    /// <para>
    /// Depletion is a state change, not a despawn: the issue allows either, and a state change
    /// keeps the object's identity (and its observers, and its scene id) intact across the
    /// empty window. The replicated <see cref="Depleted"/> flag drives
    /// <see cref="ApplyDepletedState"/> on every client, which drops the collider so a click on
    /// an empty vein never reaches the server and drops the renderer so the emptiness is
    /// visible. The server's own respawn timer — plain server clock arithmetic in
    /// <see cref="Update"/> — flips the flag back and the vein reappears everywhere at once.
    /// </para>
    /// </summary>
    public sealed class ResourceNode : NetworkBehaviour
    {
        [SerializeField] private ResourceDefinition _definition;

        // Server-owned. Clients hold a replicated copy and are refused if they try to write.
        private readonly SyncVar<int> _remaining = new();
        private readonly SyncVar<bool> _depleted = new();

        // Server-only bookkeeping: when the current empty window ends. Plain state, never
        // replicated — a client that wants a respawn countdown asks the server for one.
        private double _respawnAt;

        /// <summary>
        /// The definition that says what this node is and what it gives. May be null — a node
        /// with no definition yields nothing rather than crashing the gather loop.
        /// </summary>
        public ResourceDefinition Definition => _definition;

        /// <summary>What kind of resource this node is.</summary>
        public ResourceType Type => _definition != null ? _definition.Type : ResourceType.None;

        /// <summary>The item a harvest awards, or null when the node has nothing to give.</summary>
        public ItemDefinition Drop => _definition != null ? _definition.Drop : null;

        /// <summary>Harvests the node holds before it depletes, floored at one.</summary>
        public int DropAmount =>
            _definition != null ? _definition.DropAmount : ResourceDefinition.DefaultDropAmount;

        /// <summary>
        /// The tool tier a gatherer has to bring. Zero means the node asks for no tier — bare
        /// hands will do — and a tier above zero means a T1 tool never opens a T2 vein.
        /// </summary>
        public int GatheringTier => _definition != null ? _definition.GatheringTier : 0;

        /// <summary>
        /// The kind of tool the gatherer must hold, or <see cref="ToolKind.None"/> when bare
        /// hands suffice.
        /// </summary>
        public ToolKind RequiredTool =>
            _definition != null ? _definition.RequiredTool : ToolKind.None;

        /// <summary>Seconds the node stays depleted before the server regrows it.</summary>
        public float ResetTime =>
            _definition != null ? _definition.ResetTime : ResourceDefinition.DefaultResetTime;

        /// <summary>Seconds between two harvest ticks.</summary>
        public float GatherIntervalSeconds =>
            _definition != null
                ? _definition.GatherIntervalSeconds
                : ResourceDefinition.DefaultGatherIntervalSeconds;

        /// <summary>Metres a gatherer may be gathered from.</summary>
        public float InteractRange =>
            _definition != null ? _definition.InteractRange : ResourceDefinition.DefaultInteractRange;

        /// <summary>Harvests left. Zero once the server has taken the last one.</summary>
        public int Remaining => _remaining.Value;

        /// <summary>True while the server is waiting out the respawn timer.</summary>
        public bool Depleted => _depleted.Value;

        /// <summary>
        /// True when this node may be harvested right now: not depleted and with durability
        /// left. Readable by anyone (a UI prompt greys out on it); writable by nobody.
        /// </summary>
        public bool IsAvailable => !_depleted.Value && _remaining.Value > 0;

        /// <summary>
        /// Raised on every side whenever the replicated state moves — durability spent or the
        /// depleted flag flipping — the single hook a vein's UI or view needs to repaint. The
        /// callback comes from the <c>SyncVar.OnChange</c> handlers below, so it fires exactly
        /// when the server's truth arrives, never on a frame timer.
        /// </summary>
        public event Action StateChanged;

        /// <summary>
        /// Points this node at a definition. This is configuration, not state: the reference is
        /// baked into the prefab and read by both ends, and this setter exists for the code that
        /// builds a node in memory (<see cref="MMO.Net.ResourceNodePrefabFactory"/>) or writes
        /// one to disk (the editor baker). It is never replicated, and the server's own copy is
        /// the only one that ever decides anything.
        /// </summary>
        public void SetDefinition(ResourceDefinition definition)
        {
            _definition = definition;
        }

        public override void OnStartNetwork()
        {
            if (IsServer)
            {
                // A SyncVar is default-initialised, so an unreset node would spawn empty.
                _remaining.Value = DropAmount;
                _depleted.Value = false;
            }

            // Both sides subscribe: clients need the callbacks to drive the view, the server
            // needs them for the same repaint hook on a host.
            _remaining.OnChange += OnRemainingChanged;
            _depleted.OnChange += OnDepletedChanged;

            // Paint the state that is already true here as well: a late joiner receives the
            // current values in the spawn payload, and a change callback is not guaranteed to
            // replay for values it never saw move.
            ApplyDepletedState(_depleted.Value);
        }

        public override void OnStopNetwork()
        {
            _remaining.OnChange -= OnRemainingChanged;
            _depleted.OnChange -= OnDepletedChanged;
        }

        /// <summary>
        /// The server's respawn timer. Only a depleted node is waiting on one, and only the
        /// server runs this — a client learns the node is back from the replicated flag, never
        /// from a countdown of its own.
        /// </summary>
        private void Update()
        {
            if (!IsServer || !_depleted.Value)
                return;

            if (GatheringRules.IsRespawnDue(_respawnAt, Time.timeAsDouble))
                Respawn();
        }

        /// <summary>
        /// Server: take one harvest from this node on behalf of <paramref name="inventory"/>.
        /// Refuses — without spending any durability — when the node is unavailable, when the
        /// definition has no drop item to give, or when the bag has no room for the award.
        /// Returns true only when an item actually landed in the bag, which is the fact
        /// <see cref="PlayerGathering"/> uses to decide whether to schedule the next tick.
        /// </summary>
        /// <remarks>
        /// The award leads and the durability follows, so a full bag stops the loop instead of
        /// eating the node: durability spent without an item in hand is a hole in the world the
        /// server would then have to remember to feel bad about.
        /// </remarks>
        [Server]
        public bool TryGather(PlayerInventory inventory)
        {
            if (!IsAvailable || inventory == null)
                return false;

            ItemDefinition definition = Drop;
            if (definition == null)
            {
                Debug.LogError(
                    $"{nameof(ResourceNode)}: '{name}' has no drop item to award; nothing to gather.");
                return false;
            }

            if (!inventory.TryAddItem(definition))
                return false;

            _remaining.Value = GatheringRules.ComputeRemaining(_remaining.Value);
            if (_remaining.Value == 0)
                Deplete(Time.timeAsDouble);

            return true;
        }

        /// <summary>
        /// True when the player's tools satisfy this node's requirement — the server's gate.
        /// A named kind has to be held, and it has to reach the node's tier, so a T1 pickaxe
        /// never opens a T2 vein.
        /// </summary>
        public bool HasRequiredTool(PlayerInventory inventory)
        {
            if (inventory == null)
                return false;

            return ToolRules.Satisfies(
                inventory.Items,
                inventory.Equipment,
                RequiredTool,
                GatheringTier);
        }

        /// <summary>
        /// Unity click message: a player pressed on this node's collider. The message can only
        /// arrive on the listening process, so the owning client is the one acting here. This
        /// sends intent through <see cref="PlayerGathering.RequestGather"/> and decides nothing
        /// itself — the same shape as <see cref="TargetDummy.OnMouseDown"/>.
        /// </summary>
        private void OnMouseDown()
        {
            RequestGatherFromLocalPlayer();
        }

        /// <summary>
        /// The click's local effect: resolve the local player's gathering handler and ask it to
        /// gather from this node. Does nothing until a local player actually exists — e.g. on a
        /// dedicated server, before spawn, or while the node is depleted — which is exactly how
        /// a click on a scene-placed node behaves.
        /// </summary>
        public void RequestGatherFromLocalPlayer()
        {
            if (!IsAvailable)
                return;

            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerGathering gathering = PlayerGathering.FindLocal(self.NetworkManager);
            if (gathering == null)
                return;

            gathering.RequestGather(self);
        }

        /// <summary>
        /// Paints the replicated availability onto this side's copy of the node. Called from the
        /// <see cref="_depleted"/> change callback on every client and once from
        /// <see cref="OnStartNetwork"/> so a late joiner starts painted: an empty vein loses its
        /// collider (a click on nothing must not reach the server) and its renderer (every
        /// client sees the same empty rock until the server regrows it).
        /// </summary>
        /// <remarks>
        /// Deliberately a transform-free visual: without a <c>NetworkTransform</c> the server
        /// cannot move a shrinking mesh, but a flag every client interprets the same way costs
        /// one replicated bool and can never disagree between ends.
        /// </remarks>
        public void ApplyDepletedState(bool depleted)
        {
            if (TryGetComponent(out Collider nodeCollider))
                nodeCollider.enabled = !depleted;

            if (TryGetComponent(out Renderer nodeRenderer))
                nodeRenderer.enabled = !depleted;
        }

        /// <summary>
        /// Server: the last harvest has been taken. Books the respawn on the server's clock and
        /// publishes the flag that empties the node on every client at once.
        /// </summary>
        private void Deplete(double now)
        {
            _respawnAt = GatheringRules.ComputeRespawnAt(now, ResetTime);
            _depleted.Value = true;

            Debug.Log($"[ResourceNode {name}] depleted; respawns in {ResetTime:0.#}s.");
        }

        /// <summary>Server: the empty window is over — full durability, back on every client.</summary>
        private void Respawn()
        {
            _remaining.Value = DropAmount;
            _depleted.Value = false;

            Debug.Log($"[ResourceNode {name}] respawned with {DropAmount} harvests.");
        }

        private void OnRemainingChanged(int previous, int current, bool asServer)
        {
            StateChanged?.Invoke();
        }

        private void OnDepletedChanged(bool previous, bool current, bool asServer)
        {
            ApplyDepletedState(current);
            StateChanged?.Invoke();
        }
    }
}
