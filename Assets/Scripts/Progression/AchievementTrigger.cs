namespace MMO.Progression
{
    /// <summary>
    /// The server events an achievement can count. Values are persisted inside the achievement
    /// document, so new triggers are appended rather than inserted.
    /// </summary>
    public enum AchievementTrigger
    {
        Level = 0,
        Gather = 1,
        Craft = 2,
        RaidClear = 3,
        PvpKill = 4,
        Quest = 5,
    }
}
