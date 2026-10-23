namespace MMO.Gameplay
{
    /// <summary>
    /// The four states a player's mount can be in. The numeric values are part of the wire
    /// contract because <see cref="PlayerMount"/> replicates the state as an int in a
    /// <c>SyncVar</c>; re-ordering the enum would silently reinterpret every player's state on
    /// both ends of the connection.
    /// </summary>
    public enum MountState
    {
        Unmounted = 0,
        Mounting = 1,
        Mounted = 2,
        Flying = 3,

        /// <summary>Getting off takes a cast too; appended so the existing wire values stay put.</summary>
        Dismounting = 4,
    }
}
