using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Content;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Gameplay
{
    /// <summary>
    /// Server-only enemy brain: NavMesh pathing toward the current threat target,
    /// auto-attacking when in range, and managing the threat table.
    ///
    /// Runs entirely behind <c>IsServer</checks — the <c>NavMeshAgent</c> is disabled on
    /// clients so they never path locally. Clients only receive replicated transforms
    /// from the server's authoritative movement.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(NPCStats))]
    public sealed class EnemyBrain : NetworkBehaviour
    {
        [Header("Combat")]
        [SerializeField, Min(0f)] private float _attackRange = 2f;
        [SerializeField, Min(0)] private int _autoAttackDamage = 10;
        [SerializeField, Min(0f)] private float _attackInterval = 2f;

        [Header("Movement")]
        [SerializeField, Min(0f)] private float _chaseRange = 30f;
        [SerializeField, Min(0f)] private float _leashRange = 40f;

        private NavMeshAgent _agent;
        private NPCStats _stats;
        private AuraController _auras;
        private readonly ThreatTable _threat = new();
        private double _nextAttackTime;
        private Vector3 _spawnPosition;
        private string _resolvedEntityId;
        private Transform _resolvedTarget;

        public ThreatTable Threat => _threat;

        private AuraController Auras => _auras != null ? _auras : _auras = GetComponent<AuraController>();

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (!IsServer)
            {
                enabled = false;
                return;
            }

            _agent = GetComponent<NavMeshAgent>();
            _stats = GetComponent<NPCStats>();
            _spawnPosition = transform.position;

            if (_stats != null)
                _stats.Damaged += OnDamaged;

            // NavMeshAgent is server-only: clients never run pathing.
            if (_agent == null)
            {
                Debug.LogError($"[EnemyBrain] {name} missing NavMeshAgent");
                enabled = false;
                return;
            }

            // The prefab ships the agent disabled and only enables it on a baked zone NavMesh.
            // That keeps a test scene (and any unposed spawn) from logging placement errors.
            _agent.enabled = false;
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                Debug.LogWarning($"[EnemyBrain] {name} has no NavMesh nearby; AI disabled.");
                enabled = false;
                return;
            }

            transform.position = hit.position;
            _agent.enabled = true;
            _agent.updateRotation = true;
            _agent.updatePosition = true;
        }

        public override void OnStopNetwork()
        {
            if (_stats != null)
                _stats.Damaged -= OnDamaged;

            base.OnStopNetwork();
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (!_stats.IsAlive)
            {
                _agent.isStopped = true;
                return;
            }

            if (Auras != null && Auras.BlocksActions)
            {
                _agent.isStopped = true;
                return;
            }

            bool rooted = Auras != null && Auras.BlocksMovement;

            string targetId = _threat.CurrentTarget;
            if (string.IsNullOrEmpty(targetId))
            {
                // No target: return to spawn if leashed
                ReturnToSpawnIfNeeded();
                return;
            }

            Transform targetTransform = ResolveTarget(targetId);
            if (targetTransform == null)
            {
                // Target gone (despawned/destroyed) — remove from table
                _threat.Remove(targetId);
                return;
            }

            float distance = Vector3.Distance(transform.position, targetTransform.position);

            // Leash check: if target is too far from spawn, drop aggro
            if (distance > _leashRange)
            {
                _threat.Remove(targetId);
                _agent.isStopped = true;
                return;
            }

            if (distance <= _attackRange)
            {
                // In range: stop and auto-attack. A root stops the feet, not the swing.
                _agent.isStopped = true;
                TryAutoAttack(targetTransform);
            }
            else if (rooted)
            {
                _agent.isStopped = true;
            }
            else if (distance <= _chaseRange)
            {
                // In chase range: path to target
                _agent.isStopped = false;
                _agent.SetDestination(targetTransform.position);
            }
            else
            {
                // Target out of chase range: drop aggro
                _threat.Remove(targetId);
                ReturnToSpawnIfNeeded();
            }
        }

        private void ReturnToSpawnIfNeeded()
        {
            if (Auras != null && Auras.BlocksMovement)
            {
                _agent.isStopped = true;
                return;
            }

            float distToSpawn = Vector3.Distance(transform.position, _spawnPosition);
            if (distToSpawn > 1f)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_spawnPosition);
            }
            else
            {
                _agent.isStopped = true;
            }
        }

        private Transform ResolveTarget(string entityId)
        {
            // A target keeps its transform until it despawns, so reuse it while it lives; only a
            // changed id or a despawned target re-runs the scene scan below.
            if (_resolvedTarget != null && entityId == _resolvedEntityId)
                return _resolvedTarget;

            Transform resolved = FindTargetTransform(entityId);
            _resolvedEntityId = entityId;
            _resolvedTarget = resolved;
            return resolved;
        }

        private Transform FindTargetTransform(string entityId)
        {
            // Object id is the stable key; the name fallback keeps the existing taunt path and
            // hand-authored callers working. A real entity registry can replace both later.
            var candidates = FindObjectsByType<NPCStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (MatchesEntity(candidates[i], entityId))
                    return candidates[i].transform;
            }

            var players = FindObjectsByType<PlayerStats>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < players.Length; i++)
            {
                if (MatchesEntity(players[i], entityId))
                    return players[i].transform;
            }

            return null;
        }

        private static bool MatchesEntity(NetworkBehaviour behaviour, string entityId)
        {
            if (behaviour == null)
                return false;

            if (behaviour.name == entityId)
                return true;

            NetworkObject networkObject = behaviour.NetworkObject;
            return networkObject != null && networkObject.ObjectId.ToString() == entityId;
        }

        private void OnDamaged(PlayerInventory source, int amount)
        {
            if (!IsServer || source == null)
                return;

            NetworkObject sourceObject = source.NetworkObject;
            string sourceId = sourceObject != null ? sourceObject.ObjectId.ToString() : source.name;
            if (!string.IsNullOrEmpty(sourceId))
                _threat.Add(sourceId, ThreatRules.ThreatFor(AbilityLogic.TargetedDamage, amount));
        }

        /// <summary>
        /// Server: restore the brain after a spawn controller revives this enemy. Threat from the
        /// previous life must not carry over.
        /// </summary>
        [Server]
        public void ResetForRespawn()
        {
            _threat.Clear();
            _nextAttackTime = 0;
            _spawnPosition = transform.position;
        }

        private void TryAutoAttack(Transform targetTransform)
        {
            double now = Time.timeAsDouble;
            if (now < _nextAttackTime)
                return;

            // Validate target is alive and in range
            if (targetTransform.TryGetComponent(out NPCStats targetStats) && targetStats.IsAlive)
            {
                float distance = Vector3.Distance(transform.position, targetTransform.position);
                if (distance <= _attackRange)
                {
                    targetStats.TakeDamage(_autoAttackDamage);
                    _nextAttackTime = now + _attackInterval;
                }
            }
            else if (targetTransform.TryGetComponent(out PlayerStats targetPlayer))
            {
                float distance = Vector3.Distance(transform.position, targetTransform.position);
                if (distance <= _attackRange)
                {
                    targetPlayer.TakeDamage(_autoAttackDamage);
                    _nextAttackTime = now + _attackInterval;
                }
            }
        }

        /// <summary>
        /// Server-only: call when this enemy deals damage to an entity.
        /// </summary>
        [Server]
        public void OnDealtDamage(string targetId, int amount)
        {
            if (!IsServer)
                return;

            float threat = ThreatRules.ThreatFor(AbilityLogic.TargetedDamage, amount);
            _threat.Add(targetId, threat);
        }

        /// <summary>
        /// Server-only: call when this enemy heals an entity (generates threat).
        /// </summary>
        [Server]
        public void OnHealedTarget(string targetId, int amount)
        {
            if (!IsServer)
                return;

            float threat = ThreatRules.ThreatFor(AbilityLogic.Heal, amount);
            _threat.Add(targetId, threat);
        }

        /// <summary>
        /// Server-only: call when an entity taunts this enemy.
        /// </summary>
        [Server]
        public void OnTaunted(string taunterId)
        {
            if (!IsServer)
                return;

            _threat.Set(taunterId, ThreatRules.TauntFloor);
        }

        /// <summary>
        /// Server-only: call when a target dies or despawns.
        /// </summary>
        [Server]
        public void OnTargetRemoved(string entityId)
        {
            if (!IsServer)
                return;

            _threat.Remove(entityId);
        }
    }
}