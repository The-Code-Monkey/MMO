namespace MMO.Economy
{
    /// <summary>
    /// The stable string keys every currency-aware system agrees on. Ids, not enums, because they
    /// cross the wire and are stored in saves, and a rename must not reinterpret existing balances.
    /// </summary>
    public static class CurrencyIds
    {
        public const string Gold = "gold";
        public const string Silver = "silver";
        public const string Copper = "copper";
        public const string FactionMarks = "faction_marks";
    }
}
