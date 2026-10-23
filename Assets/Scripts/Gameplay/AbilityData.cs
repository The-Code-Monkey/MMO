using MMO.Content;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// Pure data for one active skill: what the cast costs, how long it locks the caster, how
    /// far it reaches, and which <see cref="AbilityLogic"/> the server runs when it resolves.
    ///
    /// Like <see cref="WeaponData"/> this is deliberately networking-free: mana costs and
    /// cooldowns are balance numbers, the server and the client read the same asset, and a tweak
    /// ships as an edited .asset rather than a wire patch. The runtime half — validating a cast
    /// against these numbers — lives in <see cref="AbilityCastRules"/> and
    /// <see cref="AbilityCaster"/>.
    ///
    /// The fields are private <c>[SerializeField]</c> with read-only properties, matching the
    /// rest of the project: the Inspector (and the asset YAML) is the only writer.
    /// </summary>
    [CreateAssetMenu(fileName = "New Ability Data", menuName = "MMO/Ability Data")]
    public sealed class AbilityData : ScriptableObject
    {
        [SerializeField] private string _abilityName = string.Empty;
        [SerializeField, Min(0)] private int _manaCost;
        [SerializeField, Min(0f)] private float _cooldown;
        [SerializeField, Min(0f)] private float _castTime;
        [SerializeField, Min(0f)] private float _castRange;
        [SerializeField, Min(0f)] private float _effectRadius;
        [SerializeField] private AbilityLogic _logic = AbilityLogic.TargetedDamage;
        [SerializeField, Min(0)] private int _effectPower;
        [SerializeField] private AuraData _aura;

        public string AbilityName => _abilityName;

        /// <summary>Mana the server strips from the caster when the cast is accepted.</summary>
        public int ManaCost => _manaCost;

        /// <summary>Seconds this specific slot stays locked after the cast resolves.</summary>
        public float Cooldown => _cooldown;

        /// <summary>Seconds between starting the cast and the effect landing.</summary>
        public float CastTime => _castTime;

        /// <summary>Metres the target may stand from the caster for the cast to be legal.</summary>
        public float CastRange => _castRange;

        /// <summary>
        /// Metres around the effect origin that count as being hit. Zero means the origin must
        /// coincide exactly, so authored abilities should carry a real radius.
        /// </summary>
        public float EffectRadius => _effectRadius;

        /// <summary>Which mechanic the server runs on resolve — the "ability logic".</summary>
        public AbilityLogic Logic => _logic;

        /// <summary>Damage (or healing) the effect applies, before any future scaling.</summary>
        public int EffectPower => _effectPower;

        /// <summary>The aura an <see cref="AbilityLogic.ApplyAura"/> cast applies, or null.</summary>
        public AuraData Aura => _aura;
    }
}
