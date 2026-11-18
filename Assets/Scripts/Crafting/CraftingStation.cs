using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;
using UnityEngine.AI;

namespace MMO.Crafting
{
    public sealed class CraftingStation : NetworkBehaviour
    {
        /// <summary>The small anvil's stand-off, just clear of its body.</summary>
        public const float AnvilStandOffDistance = 2f;

        /// <summary>The forge's stand-off, outside its 8 m deck but inside the crafting reach.</summary>
        public const float FurnaceStandOffDistance = 5f;

        /// <summary>
        /// How far in front of the station the player stands to use it. The forge is much larger
        /// than the anvil, so it needs its own stand-off while staying inside
        /// <see cref="PlayerCrafting.MaxCraftDistance"/>.
        /// </summary>
        public float StandOffDistance =>
            StationType == CraftingStationType.Anvil ? AnvilStandOffDistance : FurnaceStandOffDistance;

        /// <summary>Local body size of the small anvil (world 1.6 x 1 x 1.2 m).</summary>
        public static Vector3 AnvilBodyLocalSize => new(0.8f, 1f, 0.6f);

        /// <summary>Local body size of the large forge (world 8 x 6.4 x 8 m).</summary>
        public static Vector3 FurnaceBodyLocalSize => new(4f, 6.4f, 4f);

        /// <summary>The forge's body is centred above the root so it starts on the floor.</summary>
        public static Vector3 FurnaceBodyLocalCenter => new(0f, 2.7f, 0f);

        public static Vector3 BodyLocalSizeFor(CraftingStationType stationType) =>
            stationType == CraftingStationType.Anvil ? AnvilBodyLocalSize : FurnaceBodyLocalSize;

        public static Vector3 BodyLocalCenterFor(CraftingStationType stationType) =>
            stationType == CraftingStationType.Anvil ? Vector3.zero : FurnaceBodyLocalCenter;

        [SerializeField]
        private GameObject _furnaceVisual;

        [SerializeField]
        private GameObject _anvilVisual;

        private readonly SyncVar<int> _stationType = new();

        public CraftingStationType StationType => (CraftingStationType)_stationType.Value;

        /// <summary>
        /// Where the player stands to use this station: in front of it, inside the crafting reach.
        /// </summary>
        public Vector3 StandOffPosition => transform.position + transform.forward * StandOffDistance;

        private void Awake()
        {
            _stationType.OnChange += OnStationTypeChanged;
            ApplyVisuals();
        }

        private void OnDestroy()
        {
            _stationType.OnChange -= OnStationTypeChanged;
        }

        [Server]
        public void Configure(CraftingStationType stationType) => _stationType.Value = (int)stationType;

        /// <summary>Points the station at the two visual variants; for the prefab factory.</summary>
        public void SetVisuals(GameObject furnace, GameObject anvil)
        {
            _furnaceVisual = furnace;
            _anvilVisual = anvil;
            ApplyVisuals();
        }

        private void OnStationTypeChanged(int previous, int next, bool asServer) => ApplyVisuals();

        private void ApplyVisuals()
        {
            CraftingStationType stationType = StationType;
            bool showAnvil = stationType == CraftingStationType.Anvil;

            if (_anvilVisual != null)
                _anvilVisual.SetActive(showAnvil);

            // Furnace is the fallback silhouette for None and the not-yet-modelled Alchemy.
            if (_furnaceVisual != null)
                _furnaceVisual.SetActive(!showAnvil);

            Vector3 bodySize = BodyLocalSizeFor(stationType);
            Vector3 bodyCenter = BodyLocalCenterFor(stationType);

            if (TryGetComponent(out BoxCollider body))
            {
                body.size = bodySize;
                body.center = bodyCenter;
            }

            if (TryGetComponent(out NavMeshObstacle obstacle))
            {
                obstacle.size = bodySize;
                obstacle.center = bodyCenter;
            }
        }

        public void RequestOpenFromLocalPlayer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerCrafting crafting = PlayerCrafting.FindLocal(self.NetworkManager);
            if (crafting == null)
                return;

            crafting.RequestOpenStation(this);
        }
    }
}
