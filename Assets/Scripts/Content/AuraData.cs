using UnityEngine;

namespace MMO.Content
{
    /// <summary>What a status effect does while it is active.</summary>
    public enum AuraKind
    {
        Dot = 0,
        Hot = 1,
        Stun = 2,
        Root = 3,
        Buff = 4,
        Debuff = 5,
    }

    /// <summary>Which player stat a <see cref="AuraKind.Buff"/> or <see cref="AuraKind.Debuff"/> changes.</summary>
    public enum AuraStatKind
    {
        None = 0,
        MaxHealth = 1,
        MaxMana = 2,
        MoveSpeedPercent = 3,
    }

    /// <summary>
    /// A status effect definition. DoT/HoT use <see cref="MagnitudePerTick"/>; Buff/Debuff use
    /// <see cref="FlatStatBonus"/> on the selected <see cref="Stat"/>; Stun/Root use none of them.
    /// </summary>
    [CreateAssetMenu(fileName = "New Aura Data", menuName = "MMO/Aura Data")]
    public sealed class AuraData : ScriptableObject
    {
        [SerializeField] private string _auraId = string.Empty;
        [SerializeField] private AuraKind _kind = AuraKind.Buff;
        [SerializeField] private AuraStatKind _stat = AuraStatKind.None;
        [SerializeField, Min(0f)] private float _durationSeconds;
        [SerializeField, Min(0f)] private float _tickIntervalSeconds;
        [SerializeField] private int _magnitudePerTick;
        [SerializeField] private int _flatStatBonus;
        [SerializeField, Min(1)] private int _maxStacks = 1;

        public string AuraId => _auraId;
        public AuraKind Kind => _kind;
        public AuraStatKind Stat => _stat;
        public float DurationSeconds => Mathf.Max(0f, _durationSeconds);
        public float TickIntervalSeconds => Mathf.Max(0f, _tickIntervalSeconds);
        public int MagnitudePerTick => _magnitudePerTick;
        public int FlatStatBonus => _flatStatBonus;
        public int MaxStacks => Mathf.Max(1, _maxStacks);
    }
}
