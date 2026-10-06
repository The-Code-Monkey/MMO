using System;
using System.Collections.Generic;
using UnityEngine;

namespace MMO.Gameplay
{
    /// <summary>
    /// The armor half of the "you are what you wear" data pair: how much protection the piece
    /// grants, plus the <see cref="AbilityData"/> skills it injects into the wearer's active
    /// ability slots.
    ///
    /// Deliberately carries no networking, exactly like <see cref="WeaponData"/> — a balance
    /// number is not state, the server and client read the same asset, and nothing here can be
    /// patched from the wire. Mitigation from <see cref="ArmorValue"/> lands with the
    /// incoming-damage pipeline; what this issue needs from armor is the ability injection.
    /// </summary>
    [CreateAssetMenu(fileName = "New Armor Data", menuName = "MMO/Armor Data")]
    public sealed class ArmorData : ScriptableObject
    {
        [SerializeField] private string _armorName = string.Empty;
        [SerializeField, Min(0)] private int _armorValue;
        [SerializeField] private AbilityData[] _abilities = Array.Empty<AbilityData>();

        public string ArmorName => _armorName;
        public int ArmorValue => _armorValue;

        /// <summary>
        /// The skills this piece grants, in authoring order. Read-only: gear grants abilities,
        /// nothing at runtime rewrites the asset.
        /// </summary>
        public IReadOnlyList<AbilityData> Abilities => _abilities ?? Array.Empty<AbilityData>();
    }
}
