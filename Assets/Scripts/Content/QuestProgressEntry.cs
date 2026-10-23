using System;

namespace MMO.Content
{
    /// <summary>
    /// One replicated objective counter. Public fields because this is a FishNet wire type and a
    /// save-round-trip shape.
    /// </summary>
    [Serializable]
    public struct QuestProgressEntry
    {
        public string QuestId;
        public QuestObjectiveKind Kind;
        public string TargetName;
        public int Count;
    }
}
