using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A player's gathering pipeline — the skilling half of the click, and the exact shape
    /// <see cref="PlayerCombat"/> gives to attacking. The client never decides anything: clicking
    /// a resource node sends an intent (<see cref="RequestGather"/>), and the server re-checks
    /// ownership before it runs <see cref="StartGathering"/>, which validates the node's
    /// availability, the distance and the tool the node demands, then books the first tick.
    ///
    /// <para>
    /// The tick timer lives here, on the player, and only on the server: each due tick
    /// re-validates reach and tool against the node's live configuration, calls
    /// <see cref="ResourceNode.TryGather"/> to pull one harvest into this player's
    /// <see cref="PlayerInventory"/>'s replicated bag, and books the next tick — so the loop is
    /// server time all the way down. It ends when the node empties, when the gatherer walks out
    /// of reach, when the tool disappears from bag and equipment, when the bag has no room, or
    /// when the node is despawned; every one of those is a fact only the server can observe,
    /// which is why none of them is ever evaluated on the client.
    /// </para>
    ///
    /// <para>
    /// The component belongs on the player prefab, because that is the object the local client
    /// owns — a <c>[ServerRpc]</c> on the node itself would be refused by FishNet's ownership
    /// requirement (the node is server-owned), and re-implementing that check by hand is exactly
    /// the kind of "trust me, I am who I say" packet the server is supposed to refuse.
    /// </para>
    /// </summary>
    public sealed class PlayerGathering : NetworkBehaviour
    {
        // Server-only session state: which node this player is working, and when its next tick
        // lands on the server's clock. A pure client's copy is never filled, so it can hold no
        // opinion about its own gathering progress.
        private ResourceNode _node;
        private double _nextTickAt;

        private PlayerInventory _inventory;

        /// <summary>The node the server is currently gathering from, or null when idle.</summary>
        public ResourceNode GatheringNode => _node;

        /// <summary>
        /// Owner-side intent: ask the server to start gathering from <paramref name="target"/>.
        /// The client decides nothing — no reach check, no tool check, not even that the node is
        /// available; the server reads its own state when the request lands. Ownership is checked
        /// locally first (an object the client does not own gets no RPC at all) and again on the
        /// server.
        /// </summary>
        /// <remarks>
        /// The <see cref="NetworkObject"/> access is deliberately null-guarded: it is null until
        /// FishNet has initialized this behaviour (at registration or spawn), and a not-yet-
        /// initialized player cannot speak for an owner.
        /// </remarks>
        public void RequestGather(NetworkObject target)
        {
            if (target == null)
                return;

            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestGather(target);
        }

        [ServerRpc]
        private void RpcRequestGather(NetworkObject target, NetworkConnection sender = null)
        {
            // Belt and braces: FishNet already drops ServerRpcs from non-owners (RequireOwnership
            // is on), but the extra check keeps a direct server-side call from bypassing it and
            // documents that the server is the one re-validating the intent.
            if (sender == null || !OwnerMatches(sender))
                return;

            StartGathering(target);
        }

        /// <summary>
        /// The server-side decision, and the whole of it: the target must be a live
        /// <see cref="ResourceNode"/> that is still available, the gatherer must stand within
        /// its <see cref="ResourceNode.InteractRange"/>, and the bag or the equipment must hold
        /// the tool the node demands. Only then is a session booked. A fresh accepted request
        /// replaces any running one, so clicking a second node simply moves the loop.
        /// </summary>
        [Server]
        public void StartGathering(NetworkObject target)
        {
            if (target == null || !target.IsSpawned)
                return;

            if (!target.TryGetComponent(out ResourceNode node) || !node.IsAvailable)
                return;

            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return;

            if (!GatheringRules.IsWithinReach(transform.position, node.transform.position, node.InteractRange))
                return;

            if (!GatheringRules.HasRequiredTool(inventory.Items, inventory.Equipment, node.RequiredTool))
                return;

            _node = node;

            // The first harvest lands one interval from now, not instantly: the interval *is*
            // the gathering action, and a click that paid it out immediately would double the
            // node's yield for anyone who can click faster than the tick.
            _nextTickAt = GatheringRules.ComputeNextTick(Time.timeAsDouble, node.GatherIntervalSeconds);
        }

        /// <summary>
        /// Server: end the session, whether because the node emptied, the gatherer walked away,
        /// or other server code decided this player has something better to do. Idempotent.
        /// </summary>
        [Server]
        public void StopGathering()
        {
            _node = null;
        }

        /// <summary>
        /// The server's half of the tick timer. A pure client runs this every frame and does
        /// nothing, because its session state is never filled.
        /// </summary>
        private void Update()
        {
            if (!IsServer)
                return;

            // Unity's destroyed-object equality: a node that was despawned out from under the
            // session reads as null here, and the stale reference is dropped rather than
            // dereferenced on the next frame.
            ResourceNode node = _node;
            if (node == null)
            {
                _node = null;
                return;
            }

            if (!node.IsSpawned || !node.IsAvailable)
            {
                StopGathering();
                return;
            }

            double now = Time.timeAsDouble;
            if (!GatheringRules.IsTickDue(_nextTickAt, now))
                return;

            // Everything below is a re-validation, not a first check: between ticks the gatherer
            // may have walked out of reach, dropped the pickaxe, or filled the bag, and a tick
            // that ignores any of those is a tick the server should not have paid out.
            PlayerInventory inventory = Inventory;
            if (inventory == null ||
                !GatheringRules.IsWithinReach(transform.position, node.transform.position, node.InteractRange) ||
                !GatheringRules.HasRequiredTool(inventory.Items, inventory.Equipment, node.RequiredTool) ||
                !node.TryGather(inventory))
            {
                StopGathering();
                return;
            }

            // TryGather said yes, so the node still had a harvest to give — but the one just
            // taken may have been the last, in which case the loop stops here rather than on
            // the next frame's availability check.
            if (!node.IsAvailable)
            {
                StopGathering();
                return;
            }

            _nextTickAt = GatheringRules.ComputeNextTick(now, node.GatherIntervalSeconds);
        }

        /// <summary>
        /// Finds this process's player object — the spawned object owned by the local client —
        /// and returns its gathering behaviour, or null when there is none yet.
        /// </summary>
        /// <remarks>
        /// Uses the object lists only where ownership lives for the current role: a host sees its
        /// own player as a server object, a pure client as a client object. On a dedicated server
        /// there is no local owner and this returns null, which is correct — servers do not click.
        /// This stays null before spawn, so an immediately-clicked scene node is a harmless no-op.
        /// </remarks>
        public static PlayerGathering FindLocal(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.ServerManager == null || networkManager.ClientManager == null)
                return null;

            IReadOnlyDictionary<int, NetworkObject> spawned = networkManager.IsHostStarted
                ? networkManager.ServerManager.Objects.Spawned
                : networkManager.ClientManager.Objects.Spawned;

            foreach (NetworkObject nob in spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner && nob.TryGetComponent(out PlayerGathering gathering))
                    return gathering;
            }

            return null;
        }

        // Null-guarded lazy fetch so pure-logic tests can construct the component without a
        // sibling PlayerInventory and still exercise everything except the award itself.
        private PlayerInventory Inventory => _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();
    }
}
