using MMO.Crafting;

namespace MMO.UI
{
    /// <summary>
    /// What an order row says, kept pure so the panel never derives a countdown itself: the
    /// remaining time is whatever the server last sent, and a resolved state is a word.
    /// </summary>
    public static class CraftOrderUiFormat
    {
        /// <summary>The row's right-hand status: a server-sent countdown, or the resolved state.</summary>
        public static string Status(CraftOrderView order) => order.State switch
        {
            CraftOrderState.InProgress => $"{System.Math.Max(0, (int)System.Math.Ceiling(order.RemainingSeconds))}s",
            CraftOrderState.Completed => "Ready",
            CraftOrderState.Cancelled => "Cancelled",
            _ => string.Empty,
        };

        /// <summary>True when a claim can resolve the order: a completed output or a cancelled escrow.</summary>
        public static bool CanClaim(CraftOrderView order)
            => order.State == CraftOrderState.Completed || order.State == CraftOrderState.Cancelled;

        /// <summary>True while the order can still be cancelled: in progress and not yet due.</summary>
        public static bool CanCancel(CraftOrderView order)
            => order.State == CraftOrderState.InProgress && order.RemainingSeconds > 0f;

        /// <summary>True when the row has any action at all.</summary>
        public static bool HasAction(CraftOrderView order) => CanClaim(order) || CanCancel(order);

        /// <summary>The label on the row's action button; empty when the row has no action.</summary>
        public static string ActionLabel(CraftOrderView order)
        {
            if (CanClaim(order))
                return "Claim";

            return CanCancel(order) ? "Cancel" : string.Empty;
        }
    }
}
