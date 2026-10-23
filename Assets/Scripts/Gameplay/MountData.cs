using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure data for one mount: how fast it moves on the ground and in the air, whether it can
    /// fly at all, how big it is, and the model that is skinned over the generic mount body.
    ///
    /// Like <see cref="AbilityData"/> this is deliberately networking-free — balance numbers and
    /// cosmetics are not state. The server reads the same asset the client reads, so what crosses
    /// the wire is only the item name that summons the mount, which both ends resolve back to
    /// their local asset through <see cref="MountDataRegistry"/>.
    ///
    /// <para>
    /// There is no per-mount prefab. <see cref="MountPrefabFactory"/> builds one generic body from
    /// <see cref="Size"/> and drops <see cref="ModelPrefab"/> over it, so authoring a new mount is
    /// "set isFlying/speed/size, point at a model" rather than "build another prefab".
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "New Mount Data", menuName = "MMO/Mount Data")]
    public sealed class MountData : ScriptableObject
    {
        [SerializeField] private string _mountName = string.Empty;

        /// <summary>
        /// The inventory item that summons this mount. It is the registry key and what
        /// <see cref="PlayerMount"/> resolves on the server — see <see cref="ItemName"/>.
        /// </summary>
        [SerializeField] private string _itemName = string.Empty;

        [SerializeField] private bool _isFlyingMount;
        [SerializeField, Min(0.1f)] private float _groundSpeedMultiplier = 1.5f;
        [SerializeField, Min(0.1f)] private float _flightSpeedMultiplier = 2.5f;

        /// <summary>Uniform scale applied to the generic mount body.</summary>
        [SerializeField, Min(0.1f)] private float _size = 1f;

        /// <summary>Cosmetic model placed over the generic body; may be left empty.</summary>
        [SerializeField] private GameObject _modelPrefab;

        [SerializeField, Min(0f)] private float _mountCastSeconds = 1.5f;

        public string MountName => _mountName;

        /// <summary>The key both ends resolve this mount by. Must be unique in the project.</summary>
        public string ItemName => _itemName;

        public bool IsFlyingMount => _isFlyingMount;

        public float GroundSpeedMultiplier => Mathf.Max(0.1f, _groundSpeedMultiplier);

        public float FlightSpeedMultiplier => Mathf.Max(0.1f, _flightSpeedMultiplier);

        public float Size => Mathf.Max(0.1f, _size);

        public GameObject ModelPrefab => _modelPrefab;

        /// <summary>Seconds the server waits between "use mount" and "mounted".</summary>
        public float MountCastSeconds => Mathf.Max(0f, _mountCastSeconds);

        // An asset deserialized from disk already carries its key, so loading it registers it;
        // one built in code registers after a test or bootstrap gives it a key.
        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_itemName))
                MountDataRegistry.Register(this);
        }

        private void OnDisable() => MountDataRegistry.Unregister(this);
    }
}
