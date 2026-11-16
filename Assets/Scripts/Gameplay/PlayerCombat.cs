using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Object;
using MMO.Content;
using MMO.Inventory;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// A player's attack pipeline. The client never decides a hit landed: clicking a target dummy
    /// sends an intent (<see cref="RequestAttack"/>), and the server re-checks ownership before it
    /// runs <see cref="Attack"/>, which validates the target, the equipped weapon, the distance
    /// against the <see cref="WeaponData"/> the player actually holds, the facing cone, and a
    /// clear line of sight (see <see cref="SightRules"/> / <see cref="IServerSight"/>), so a shot
    /// cannot pass through a wall.
    ///
    /// <see cref="Attack"/> understands the two damageable shapes this project owns: the
    /// networked <see cref="NPCStats"/> every zone enemy carries, and the older
    /// <see cref="TargetDummy"/> the combat tests and training dummy use.
    /// </summary>
    public sealed class PlayerCombat : NetworkBehaviour
    {
        // Serialized so the editor can bake the starting weapon straight onto the prefab. It is a
        // shared ScriptableObject asset, never a per-player reference, so no SyncVar is needed:
        // the server reads the same asset in every case. Clients may inspect it for UI and receive
        // what is actually equipped; only the server ever assigns it (Equip is [Server]).
        [SerializeField] private WeaponData _equippedWeapon;

        // Where the server casts its line-of-sight ray from, above the capsule's pivot, and how
        // wide the cone in front of the player that a target must be inside is. Serialized so a
        // designer can tune reach without a code change; the values are read only on the server.
        [SerializeField] private float _eyeHeight = PhysicsSight.DefaultEyeHeight;
        [SerializeField] private float _attackHalfAngleDegrees = 60f;

        private IServerSight _sight;

        /// <summary>
        /// The line-of-sight check the server consults before a hit lands. Defaults to a real
        /// raycast; a test can replace it to force a clear or blocked line without a scene. Only
        /// ever read from the server's side of a validated request, so a client cannot influence it.
        /// </summary>
        public IServerSight Sight
        {
            get => _sight ??= new PhysicsSight();
            set => _sight = value;
        }

        /// <summary>The weapon this player is holding, or null when they spawn unarmed.</summary>
        public WeaponData EquippedWeapon => _equippedWeapon;

        /// <summary>
        /// Swaps the equipped weapon on the server. Authoritative, so a cheating client cannot
        /// hand itself a stronger blade; the only writer is server-side code.
        /// </summary>
        [Server]
        public void Equip(WeaponData weapon)
        {
            _equippedWeapon = weapon;
        }

        /// <summary>
        /// Client input path for attacking <paramref name="target"/>. This only sends intent; it
        /// never decides anything. Ownership is checked locally first (an object the client does
        /// not own gets no RPC at all) and again on the server.
        /// </summary>
        /// <remarks>
        /// The <see cref="NetworkObject"/> access is deliberately null-guarded: it is null until
        /// FishNet has initialized this behaviour (at registration or spawn), and a not-yet-
        /// initialized player cannot speak for an owner.
        /// </remarks>
        public void RequestAttack(NetworkObject target)
        {
            if (target == null)
                return;

            NetworkObject self = NetworkObject;
            if (self == null || !self.IsOwner)
                return;

            RpcRequestAttack(target);
        }

        [ServerRpc]
        private void RpcRequestAttack(NetworkObject target, NetworkConnection sender = null)
        {
            // Belt and braces: FishNet already drops ServerRpcs from non-owners (RequireOwnership
            // is on), but the extra check keeps a direct server-side call from bypassing it and
            // documents that the server is the one re-validating the intent.
            if (sender == null || !OwnerMatches(sender))
                return;

            Attack(target);
        }

        /// <summary>
        /// The server-side decision. Validates everything here, never on the client:
        /// the instigator must actually hold a weapon, the target must be a live damageable object,
        /// it must be within the weapon's <see cref="WeaponData.AttackRange">attack range</see>, in
        /// front of the attacker's facing cone, and have a clear line of sight to it (no wall in
        /// between). Only then does damage land.
        /// </summary>
        [Server]
        public void Attack(NetworkObject target)
        {
            AuraController auras = GetComponent<AuraController>();
            if (auras != null && auras.BlocksActions)
                return;

            WeaponData weapon = _equippedWeapon;
            if (weapon == null)
                return;

            if (target == null || !target.IsSpawned)
                return;

            NPCStats npc = null;
            TargetDummy dummy = null;
            Vector3 targetPosition;
            GameObject targetObject;

            if (target.TryGetComponent(out npc))
            {
                targetPosition = npc.transform.position;
                targetObject = npc.gameObject;
            }
            else if (target.TryGetComponent(out dummy))
            {
                targetPosition = dummy.transform.position;
                targetObject = dummy.gameObject;
            }
            else
            {
                return;
            }

            Vector3 eye = transform.position + Vector3.up * _eyeHeight;
            bool raycastClear = Sight.HasLineOfSight(eye, targetPosition, targetObject);

            if (!IsVisibleToAttack(eye, transform.forward, targetPosition,
                    weapon.AttackRange, _attackHalfAngleDegrees, raycastClear))
            {
                return;
            }

            // Continuum wear: the authored weapon damage is scaled by the condition of the item
            // actually worn in the main hand. A broken weapon reads as zero and is refused before
            // any damage lands, while an unworn or indestructible weapon keeps its full value.
            PlayerInventory inventory = GetComponent<PlayerInventory>();
            int damage = weapon.AutoAttackDamage;
            if (inventory != null)
                damage = DegradationRules.EffectiveItemStat(damage, inventory.GetEquipped(EquipmentSlot.MainHand));

            if (damage <= 0)
                return;

            if (npc != null)
                npc.ApplyDamage(damage, inventory);
            else
                dummy.TakeDamage(damage);
        }

        /// <summary>
        /// The whole visibility verdict in one pure function: in range, inside the facing cone and
        /// with a clear line. The raycast verdict is a boolean so the geometry can be pinned in an
        /// EditMode test without a physics scene, exactly like the other rule surfaces.
        /// </summary>
        public static bool IsVisibleToAttack(
            Vector3 eyePosition,
            Vector3 forward,
            Vector3 targetPosition,
            float attackRange,
            float halfAngleDegrees,
            bool raycastClear)
        {
            return SightRules.CanSee(
                IsWithinAttackRange(eyePosition, targetPosition, attackRange),
                SightRules.WithinCone(eyePosition, forward, targetPosition, halfAngleDegrees),
                raycastClear);
        }

        /// <summary>
        /// A target is in range when its position is strictly closer than the weapon's range.
        /// "Strictly": an attacker parked exactly on the range boundary has missed.
        /// </summary>
        public static bool IsWithinAttackRange(Vector3 attackerPosition, Vector3 targetPosition, float attackRange)
        {
            return attackRange > 0f && Vector3.Distance(attackerPosition, targetPosition) < attackRange;
        }

        /// <summary>
        /// Finds this process's player object — the spawned object owned by the local client — and
        /// returns its combat behaviour, or null when there is none yet.
        /// </summary>
        /// <remarks>
        /// Uses the object lists only where ownership lives for the current role: a host sees its
        /// own player as a server object, a pure client as a client object. On a dedicated server
        /// there is no local owner and this returns null, which is correct — servers do not click.
        /// This stays null before spawn, so an immediately-clicked scene object is a harmless no-op.
        /// </remarks>
        public static PlayerCombat FindLocal(NetworkManager networkManager)
        {
            if (networkManager == null || networkManager.ServerManager == null || networkManager.ClientManager == null)
                return null;

            IReadOnlyDictionary<int, NetworkObject> spawned = networkManager.IsHostStarted
                ? networkManager.ServerManager.Objects.Spawned
                : networkManager.ClientManager.Objects.Spawned;

            foreach (NetworkObject nob in spawned.Values)
            {
                if (nob != null && nob.IsSpawned && nob.IsOwner && nob.TryGetComponent(out PlayerCombat combat))
                    return combat;
            }

            return null;
        }
    }
}