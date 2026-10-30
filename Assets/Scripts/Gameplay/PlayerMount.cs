using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using MMO.Client;
using MMO.Inventory;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MMO.Gameplay
{
    /// <summary>
    /// The networked shell of the mount system: the single door a client can knock on to mount,
    /// take off, land or dismount, and the state the server replicates for every observer.
    ///
    /// <para>
    /// The client sends an intent and decides nothing. The server resolves the item from
    /// <see cref="PlayerInventory"/>, checks it is worn in the mount slot, runs the pure
    /// <see cref="MountStateMachine"/>, then drives <see cref="PlayerMover"/>'s speed and flight
    /// mode. The values the wire carries are the state, the mount item name and the cast progress,
    /// so the client cannot author a speed or a position.
    /// </para>
    ///
    /// <para>
    /// Visuals are cosmetic and client-local: every client resolves the replicated item name to
    /// its own <see cref="MountData"/>, builds the generic body with
    /// <see cref="MountPrefabFactory"/>, and parents it to the player. A dedicated server builds
    /// no visual at all. Nothing about the mount is a network object, so there is no prefab
    /// registration or spawn order to keep in sync.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerMount : NetworkBehaviour, IMountSource
    {
        // Wire contract: replicated as an int, so the enum values must never be re-ordered.
        private readonly SyncVar<int> _state = new();

        // Only the key crosses the wire; each end resolves it to its own asset.
        private readonly SyncVar<string> _mountItemName = new();

        // 0..1 through the current mount/dismount cast, so a client can draw the bar without
        // sharing the server's clock.
        private readonly SyncVar<float> _castProgress = new();

        private MountStateMachine _machine;
        private PlayerMover _mover;
        private PlayerInventory _inventory;
        private GameObject _visual;
        private MountData _visualData;
        private string _visualItemName;
        private Transform _riderVisual;
        private Vector3 _riderOriginalLocalPosition;

        /// <summary>The replicated state. Undeclared values read as <see cref="MountState.Unmounted"/>.</summary>
        public MountState State => Normalize(_state.Value);

        /// <summary>The item that summoned the active mount, or empty when unmounted.</summary>
        public string MountItemName => _mountItemName.Value;

        /// <summary>True on the ground, in the air, or getting off, i.e. a mount is active.</summary>
        public bool IsMounted =>
            State == MountState.Mounted || State == MountState.Flying || State == MountState.Dismounting;

        public bool IsFlying => State == MountState.Flying;

        /// <inheritdoc />
        public float CastProgress => _castProgress.Value;

        /// <summary>Raised on the server and every client whenever the replicated state changes.</summary>
        public event Action StateChanged;

        /// <inheritdoc />
        public event Action MountChanged;

        private PlayerMover Mover => _mover != null ? _mover : _mover = GetComponent<PlayerMover>();

        private PlayerInventory Inventory =>
            _inventory != null ? _inventory : _inventory = GetComponent<PlayerInventory>();

        private Transform RiderVisual
        {
            get
            {
                if (_riderVisual != null)
                    return _riderVisual;

                PlayerCharacterVisual visual = GetComponentInChildren<PlayerCharacterVisual>(true);
                if (visual == null)
                    return null;

                _riderVisual = visual.transform;
                _riderOriginalLocalPosition = _riderVisual.localPosition;
                return _riderVisual;
            }
        }

        public override void OnStartNetwork()
        {
            _state.OnChange += OnStateReplicated;
            _mountItemName.OnChange += OnMountItemReplicated;
            _castProgress.OnChange += OnCastProgressReplicated;

            if (IsServer)
            {
                PlayerInventory inventory = Inventory;
                if (inventory != null)
                    inventory.ContainersChanged += OnInventoryChanged;

                RefreshUnmountedSpeed();
            }
        }

        public override void OnStartClient()
        {
            RefreshVisual();
        }

        public override void OnStopNetwork()
        {
            _state.OnChange -= OnStateReplicated;
            _mountItemName.OnChange -= OnMountItemReplicated;
            _castProgress.OnChange -= OnCastProgressReplicated;

            PlayerInventory inventory = Inventory;
            if (inventory != null)
                inventory.ContainersChanged -= OnInventoryChanged;

            DestroyVisual();
        }

        /// <summary>
        /// Owner-side intent: use the mount item in this bag. The server decides whether the item
        /// is real and whether the player carries it; the client's word is not evidence.
        /// </summary>
        public void RequestMount(string itemName)
        {
            if (!IsOwner)
                return;

            RpcRequestMount(itemName);
        }

        /// <summary>Owner-side intent: take off. Refused outside a flight-allowed zone.</summary>
        public void RequestTakeoff()
        {
            if (!IsOwner)
                return;

            RpcRequestTakeoff();
        }

        /// <summary>Owner-side intent: return from flight to ground movement.</summary>
        public void RequestLand()
        {
            if (!IsOwner)
                return;

            RpcRequestLand();
        }

        /// <summary>Owner-side intent: remove the mount. Refused mid-air.</summary>
        public void RequestDismount()
        {
            if (!IsOwner)
                return;

            RpcRequestDismount();
        }

        [ServerRpc]
        private void RpcRequestMount(string itemName, NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender))
                return;

            // Already mounting or mounted: a repeated click must not restart the cast.
            if (_machine != null)
                return;

            MountData data = MountDataRegistry.Resolve(itemName);
            if (data == null)
            {
                Debug.LogWarning($"{nameof(PlayerMount)}: no mount data for item '{itemName}'.");
                return;
            }

            PlayerInventory inventory = Inventory;
            if (inventory == null || !IsEquippedMount(itemName))
                return;

            var machine = new MountStateMachine(data);
            if (!machine.RequestMount(Time.timeAsDouble))
                return;

            _machine = machine;
            _mountItemName.Value = itemName;
            ApplyState(machine);
        }

        [ServerRpc]
        private void RpcRequestTakeoff(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || _machine == null)
                return;

            if (!_machine.RequestTakeoff(ZoneRiskConfig.ResolveAllowsFlyingAt(transform.position)))
                return;

            ApplyState(_machine);
        }

        [ServerRpc]
        private void RpcRequestLand(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || _machine == null)
                return;

            if (!_machine.RequestLand())
                return;

            LandOnGround();
            ApplyState(_machine);
        }

        [ServerRpc]
        private void RpcRequestDismount(NetworkConnection sender = null)
        {
            if (sender == null || !OwnerMatches(sender) || _machine == null)
                return;

            if (!_machine.RequestDismount(Time.timeAsDouble))
                return;

            ApplyState(_machine);
        }

        private void Update()
        {
            if (IsServer)
                ServerTick();

            if (IsOwner)
                HandleMountKey();

            if (IsClient)
                SyncVisualPose();
        }

        private void ServerTick()
        {
            if (_machine == null)
                return;

            double now = Time.timeAsDouble;
            MountState before = _machine.State;
            _machine.Tick(now);
            if (_machine.State != before)
                ApplyState(_machine);

            if (_machine.State == MountState.Unmounted)
            {
                _machine = null;
                return;
            }

            UpdateCastProgress(now);

            // The item is the authority for having the mount: losing it dismounts.
            if (_machine.IsMounted && !HasMountItem())
                Disengage();
        }

        /// <summary>
        /// Owner-side default binding: G mounts the item worn in the mount slot, dismounts when
        /// riding, and lands when flying. The server still validates every request.
        /// </summary>
        private void HandleMountKey()
        {
            ClientInput input = ClientInput.Current;
            if (input != null)
            {
                if (!input.WasPressed(GameAction.Mount))
                    return;
            }
            else
            {
                Keyboard keyboard = Keyboard.current;
                if (keyboard == null || !keyboard.gKey.wasPressedThisFrame)
                    return;

                // Typing "g" into chat or a price field must not mount.
                if (IsTypingInUi())
                    return;
            }

            if (State == MountState.Unmounted)
            {
                string itemName = FirstMountItem();
                if (!string.IsNullOrEmpty(itemName))
                    RequestMount(itemName);
                return;
            }

            if (State == MountState.Mounted)
                RequestDismount();
            else if (State == MountState.Flying)
                RequestLand();
        }

        private static bool IsTypingInUi()
        {
            EventSystem system = EventSystem.current;
            GameObject selected = system != null ? system.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }

        private string FirstMountItem()
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null)
                return string.Empty;

            ItemInstance equipped = inventory.GetEquipped(EquipmentSlot.Mount);
            return !equipped.IsEmpty && MountDataRegistry.Resolve(equipped.ItemName) != null
                ? equipped.ItemName
                : string.Empty;
        }

        private bool HasMountItem()
        {
            return Inventory == null || IsEquippedMount(_mountItemName.Value);
        }

        /// <summary>
        /// True only when <paramref name="itemName"/> is worn in the mount slot. Carrying the item
        /// in the bag is not enough: the player has to equip it first.
        /// </summary>
        private bool IsEquippedMount(string itemName)
        {
            PlayerInventory inventory = Inventory;
            if (inventory == null || string.IsNullOrEmpty(itemName))
                return false;

            ItemInstance equipped = inventory.GetEquipped(EquipmentSlot.Mount);
            return !equipped.IsEmpty && equipped.ItemName == itemName;
        }

        /// <summary>Lands then unmounts, the forced path taken when the item goes away.</summary>
        private void Disengage()
        {
            if (_machine == null)
                return;

            if (_machine.IsFlying)
            {
                _machine.RequestLand();
                LandOnGround();
            }

            _machine.Dismount();
            ApplyState(_machine);
            _machine = null;
        }

        /// <summary>
        /// Server: push the machine's state into the replicated value and the mover. Speed and
        /// flight are derived from state here, so a client can never set them.
        /// </summary>
        private void ApplyState(MountStateMachine machine)
        {
            MountState state = machine.State;
            _state.Value = (int)state;
            _castProgress.Value = machine.CastProgress(Time.timeAsDouble);

            PlayerMover mover = Mover;
            if (mover != null)
            {
                mover.SetSpeedMultiplier(state == MountState.Unmounted
                    ? UnmountedSpeedMultiplier()
                    : machine.SpeedMultiplier);
                mover.SetFlight(state == MountState.Flying);
            }

            // The five trailing slots are placeable while the player is still on the mount, and
            // close as soon as they are off. Items already in them stay and count as overload.
            PlayerInventory inventory = Inventory;
            if (inventory != null)
            {
                inventory.SetMountCapacity(
                    state == MountState.Mounted ||
                    state == MountState.Flying ||
                    state == MountState.Dismounting);
            }

            if (state == MountState.Unmounted)
                _mountItemName.Value = string.Empty;
        }

        private void UpdateCastProgress(double now)
        {
            if (_machine == null)
                return;

            if (_machine.State == MountState.Mounting || _machine.State == MountState.Dismounting)
                _castProgress.Value = _machine.CastProgress(now);
        }

        /// <summary>
        /// The bag keeps every item when the player dismounts; anything over the base capacity is
        /// an overload that slows the walk. Mounted players are within the extra slots, so this
        /// only applies while unmounted.
        /// </summary>
        private float UnmountedSpeedMultiplier()
        {
            PlayerInventory inventory = Inventory;
            int itemCount = inventory != null ? inventory.ItemCount : 0;
            return MountRules.UnmountedSpeedMultiplier(itemCount, PlayerInventory.BaseSlotCount);
        }

        private void OnInventoryChanged()
        {
            RefreshUnmountedSpeed();
        }

        private void RefreshUnmountedSpeed()
        {
            if (!IsServer || IsMounted)
                return;

            Mover?.SetSpeedMultiplier(UnmountedSpeedMultiplier());
        }

        /// <summary>Server: drop a landed player onto the NavMesh at their current X/Z.</summary>
        private void LandOnGround()
        {
            PlayerMover mover = Mover;
            if (mover == null)
                return;

            Vector3 position = transform.position;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, 50f, NavMesh.AllAreas))
                mover.SetAuthoritativePosition(new Vector3(position.x, hit.position.y, position.z));
        }

        private void OnStateReplicated(int previous, int next, bool asServer)
        {
            StateChanged?.Invoke();
            MountChanged?.Invoke();
            RefreshVisual();
        }

        private void OnCastProgressReplicated(float previous, float next, bool asServer)
        {
            MountChanged?.Invoke();
        }

        private void OnMountItemReplicated(string previous, string next, bool asServer)
        {
            RefreshVisual();
        }

        /// <summary>
        /// Client: keep the cosmetic body in step with the replicated state. Rebuilt only when the
        /// mount changes, so ground ↔ flight does not churn the model.
        /// </summary>
        private void RefreshVisual()
        {
            if (!IsClient || !IsSpawned)
            {
                DestroyVisual();
                return;
            }

            if (!IsMounted)
            {
                DestroyVisual();
                return;
            }

            MountData data = MountDataRegistry.Resolve(_mountItemName.Value);
            if (data == null)
            {
                DestroyVisual();
                return;
            }

            if (_visual != null && _visualItemName == data.ItemName)
            {
                ApplyRiderPose(data.RiderOffset);
                return;
            }

            DestroyVisual();
            _visual = MountPrefabFactory.Create(data);
            _visualData = data;
            _visualItemName = data.ItemName;

            _visual.transform.SetParent(transform, false);
            _visual.transform.localPosition = data.VisualOffset;
            _visual.transform.localRotation = Quaternion.Euler(0f, data.VisualYawOffset, 0f);
            ApplyRiderPose(data.RiderOffset);
        }

        private void SyncVisualPose()
        {
            if (!IsMounted || _visual == null || _visualData == null)
                return;

            Transform rider = RiderVisual;
            if (rider == null)
                return;

            _visual.transform.rotation =
                rider.rotation * Quaternion.Euler(0f, _visualData.VisualYawOffset, 0f);
        }

        private void ApplyRiderPose(Vector3 offset)
        {
            if (!IsClient)
                return;

            Transform rider = RiderVisual;
            if (rider == null)
                return;

            rider.localPosition = _riderOriginalLocalPosition + offset;
        }

        private void DestroyVisual()
        {
            ApplyRiderPose(Vector3.zero);

            if (_visual == null)
            {
                _visualData = null;
                _visualItemName = null;
                return;
            }

            if (Application.isPlaying)
                Destroy(_visual);
            else
                DestroyImmediate(_visual);

            _visual = null;
            _visualData = null;
            _visualItemName = null;
        }

        private static MountState Normalize(int raw)
        {
            return raw >= (int)MountState.Unmounted && raw <= (int)MountState.Dismounting
                ? (MountState)raw
                : MountState.Unmounted;
        }
    }
}
