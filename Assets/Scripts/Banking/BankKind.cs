namespace MMO.Banking
{
    /// <summary>
    /// What a bank teller opens: the player's personal vault or their guild's shared vault. The
    /// teller is generic — the same prefab and the same window serve both; only this value and the
    /// source the window binds to differ.
    /// </summary>
    public enum BankKind
    {
        Personal = 0,
        Guild = 1,
    }
}
