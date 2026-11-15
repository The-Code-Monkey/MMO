using MMO.Inventory;
using UnityEngine;

namespace MMO.Progression
{
    /// <summary>
    /// One authored achievement: what it counts, how many, and what completing it pays. It is an
    /// asset, not code, so content can add achievements without a build of the runtime.
    ///
    /// <para>
    /// The reward is deliberately simple — gold and/or one item — because an achievement is a
    /// one-shot grant, not a loot roll. The server pays it once and records that it did, so a
    /// replayed event can never double-pay.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "New Achievement", menuName = "MMO/Achievement Data")]
    public sealed class AchievementData : ScriptableObject
    {
        [SerializeField] private string _achievementId = string.Empty;
        [SerializeField] private string _title = string.Empty;
        [SerializeField] private string _description = string.Empty;
        [SerializeField] private AchievementTrigger _trigger = AchievementTrigger.Level;
        [SerializeField, Min(1)] private int _threshold = 1;
        [SerializeField, Min(0)] private int _rewardGold;
        [SerializeField] private ItemDefinition _rewardItem;

        /// <summary>The stable key persisted in the player's record; must be unique.</summary>
        public string AchievementId => _achievementId;

        public string Title => string.IsNullOrEmpty(_title) ? _achievementId : _title;

        public string Description => _description;

        public AchievementTrigger Trigger => _trigger;

        /// <summary>How many events complete it. A non-positive authored value reads as one.</summary>
        public int Threshold => Mathf.Max(1, _threshold);

        public int RewardGold => Mathf.Max(0, _rewardGold);

        /// <summary>The single item granted on completion, or null for a gold-only award.</summary>
        public ItemDefinition RewardItem => _rewardItem;

        // A loaded asset registers itself; one created in code registers once it is named, which
        // is what lets tests build definitions without a Resources folder.
        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_achievementId))
                AchievementRegistry.Register(this);
        }

        private void OnDisable() => AchievementRegistry.Unregister(this);
    }
}
