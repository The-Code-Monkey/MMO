using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure data for one weapon archetype: its name, how hard the auto-attack hits, and how far
    /// away the target can stand. This is the "you are what you wear" data half — an asset you
    /// right-click into existence from the Project window, never a scene object.
    ///
    /// Deliberately carries no networking. A balance number is not state: the server reads the
    /// same asset the client reads, so there is nothing to replicate and nothing for a client to
    /// lie about. Shipping a tweak means shipping an edited .asset, which diffs cleanly and
    /// cannot be patched from the wire.
    ///
    /// The fields are private <c>[SerializeField]</c> with read-only properties, matching the
    /// rest of the project: the Inspector (and the asset YAML) is the only writer, everything
    /// else is a reader.
    /// </summary>
    [CreateAssetMenu(fileName = "New Weapon Data", menuName = "MMO/Weapon Data")]
    public sealed class WeaponData : ScriptableObject
    {
        [SerializeField] private string _weaponName = string.Empty;
        [SerializeField, Min(0)] private int _autoAttackDamage;
        [SerializeField, Min(0f)] private float _attackRange;

        public string WeaponName => _weaponName;
        public int AutoAttackDamage => _autoAttackDamage;
        public float AttackRange => _attackRange;
    }
}
