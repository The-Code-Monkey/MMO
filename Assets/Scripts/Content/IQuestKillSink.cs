namespace MMO.Content
{
    /// <summary>
    /// Receives kill credit for a quest. The loot component talks to this interface so it does not
    /// depend on the quest-log shell, which lands in a later step.
    /// </summary>
    public interface IQuestKillSink
    {
        void NotifyKill(string targetName);
    }
}
