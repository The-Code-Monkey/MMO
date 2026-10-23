using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Server-only boss controller: owns a <see cref="BossStateMachine"/> and bridges its
    /// phase transitions to authored behaviours (spawn adds, swap ability sets, enrage buff).
    /// Replicates <see cref="Phase"/> as a <c>SyncVar</c> so the target frame and clients
    /// can display the current phase.
    /// </summary>
    [RequireComponent(typeof(NPCStats))]
    [RequireComponent(typeof(EnemyBrain))]
    public sealed class BossController : NetworkBehaviour
    {
        [Header("Add Spawning")]
        [SerializeField] private GameObject _addPrefab;
        [SerializeField, Min(0)] private int _addsPerWave = 3;
        [SerializeField, Min(0f)] private float _addSpawnRadius = 5f;

        private NPCStats _stats;
        private EnemyBrain _brain;
        private readonly BossStateMachine _machine = new();
        private double _phaseStartTime;

        private readonly SyncVar<BossPhase> _syncPhase = new();

        public BossPhase Phase => _syncPhase.Value;
        public BossStateMachine StateMachine => _machine;

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (!IsServer)
            {
                enabled = false;
                return;
            }

            _stats = GetComponent<NPCStats>();
            _brain = GetComponent<EnemyBrain>();

            if (_stats == null)
            {
                Debug.LogError($"[BossController] {name} missing NPCStats");
                enabled = false;
                return;
            }

            _phaseStartTime = Time.timeAsDouble;
            _syncPhase.Value = _machine.Phase;

            _stats.Changed += OnHealthChanged;
            _syncPhase.OnChange += OnPhaseChanged;
        }

        public override void OnStopNetwork()
        {
            if (_stats != null)
                _stats.Changed -= OnHealthChanged;

            _syncPhase.OnChange -= OnPhaseChanged;

            base.OnStopNetwork();
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (!_stats.IsAlive)
                return;

            float healthFraction = (float)_stats.Health / NPCStats.MaxHealth;
            int liveAdds = CountLiveAdds();
            double secondsInPhase = Time.timeAsDouble - _phaseStartTime;

            BossPhase newPhase = _machine.Evaluate(healthFraction, liveAdds, secondsInPhase);

            if (newPhase != _machine.Phase)
            {
                _phaseStartTime = Time.timeAsDouble;
                _syncPhase.Value = newPhase;
                OnPhaseEntered(newPhase);
            }
        }

        private void OnHealthChanged()
        {
            // Health changed — Evaluate will run in Update and transition if needed.
        }

        private void OnPhaseChanged(BossPhase previous, BossPhase current, bool asServer)
        {
            Debug.Log($"[BossController {name}] Phase: {previous} -> {current}");
        }

        private void OnPhaseEntered(BossPhase phase)
        {
            switch (phase)
            {
                case BossPhase.Adds:
                    SpawnAdds();
                    break;
                case BossPhase.Enrage:
                    ApplyEnrage();
                    break;
                case BossPhase.Defeated:
                    OnDefeated();
                    break;
            }
        }

        private void SpawnAdds()
        {
            if (_addPrefab == null)
            {
                Debug.LogWarning($"[BossController] {name} has no add prefab assigned");
                return;
            }

            for (int i = 0; i < _addsPerWave; i++)
            {
                Vector3 spawnPos = transform.position + Random.insideUnitSphere * _addSpawnRadius;
                spawnPos.y = transform.position.y;

                // Spawn via NetworkManager: instantiate first, then spawn the instance
                if (NetworkManager != null && NetworkManager.ServerManager != null && NetworkManager.IsServerStarted)
                {
                    GameObject instance = Instantiate(_addPrefab);
                    instance.transform.position = spawnPos;
                    instance.transform.rotation = Quaternion.identity;
                    instance.SetActive(true);

                    NetworkManager.ServerManager.Spawn(instance);
                }
            }
        }

        private int CountLiveAdds()
        {
            // In a full implementation, this would track spawned adds.
            // For now, return 0 to allow Enrage transition.
            // TODO: Track spawned adds via a list.
            return 0;
        }

        private void ApplyEnrage()
        {
            // Could increase damage, speed, etc. on the EnemyBrain or stats
            Debug.Log($"[BossController {name}] ENRAGE!");
        }

        private void OnDefeated()
        {
            // Stop brain, disable agent
            if (_brain != null)
            {
                _brain.enabled = false;
            }

            Debug.Log($"[BossController {name}] DEFEATED!");
        }
    }
}