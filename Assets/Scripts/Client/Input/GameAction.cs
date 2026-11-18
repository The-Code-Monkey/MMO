namespace MMO.Client
{
    /// <summary>
    /// The client actions a player may rebind. Their enum names are the action names in the
    /// runtime <c>InputActionAsset</c>, so the persisted overrides and the UI rows cannot drift
    /// from what gameplay reads.
    /// </summary>
    public enum GameAction
    {
        Ability1,
        Ability2,
        Ability3,
        Ability4,
        Ability5,
        Ability6,
        MoveForward,
        MoveBack,
        StrafeLeft,
        StrafeRight,
        TurnLeft,
        TurnRight,
        OrbitCamera,
        FaceCamera,
        Mount,
        Interact,
        Trade,
        Auction,
        Mail,
        OpenSettings,
    }

    /// <summary>Small helpers shared by the input service and the settings panel.</summary>
    public static class GameActions
    {
        /// <summary>The six action-bar slots, in bar order.</summary>
        public static readonly GameAction[] Abilities =
        {
            GameAction.Ability1,
            GameAction.Ability2,
            GameAction.Ability3,
            GameAction.Ability4,
            GameAction.Ability5,
            GameAction.Ability6,
        };

        /// <summary>Human-readable label for a settings row.</summary>
        public static string DisplayName(GameAction action)
        {
            return action switch
            {
                GameAction.Ability1 => "Ability 1",
                GameAction.Ability2 => "Ability 2",
                GameAction.Ability3 => "Ability 3",
                GameAction.Ability4 => "Ability 4",
                GameAction.Ability5 => "Ability 5",
                GameAction.Ability6 => "Ability 6",
                GameAction.MoveForward => "Move Forward",
                GameAction.MoveBack => "Move Back",
                GameAction.StrafeLeft => "Strafe Left",
                GameAction.StrafeRight => "Strafe Right",
                GameAction.TurnLeft => "Turn Left",
                GameAction.TurnRight => "Turn Right",
                GameAction.OrbitCamera => "Orbit Camera",
                GameAction.FaceCamera => "Face Camera",
                GameAction.Mount => "Mount",
                GameAction.Interact => "Interact",
                GameAction.Trade => "Trade",
                GameAction.Auction => "Auction",
                GameAction.Mail => "Mailbox",
                GameAction.OpenSettings => "Settings",
                _ => action.ToString(),
            };
        }
    }
}
