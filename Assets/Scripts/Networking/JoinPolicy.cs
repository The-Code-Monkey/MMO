namespace MMO.Networking
{
    public sealed class JoinPolicy
    {
        public const int MaxNameLength = 24;

        public bool Approve(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return false;

            return playerName.Trim().Length <= MaxNameLength;
        }
    }
}
