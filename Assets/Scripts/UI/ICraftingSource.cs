using System;
using System.Collections.Generic;
using MMO.Crafting;

namespace MMO.UI
{
    /// <summary>
    /// What a crafting panel reads: the player's crafting progress, the station that is open, the
    /// recipe offers and craft orders the server sent for it, and the intents the panel is
    /// allowed to express (craft a named recipe, claim a resolved order).
    ///
    /// <para>
    /// The panel never asks the registry what exists — <see cref="AvailableRecipes"/> is a
    /// server-built snapshot, so a station shows exactly the recipes the server decided that
    /// player may see (right station type, met level), and nothing else.
    /// </para>
    /// </summary>
    public interface ICraftingSource
    {
        int CraftingXp { get; }
        int CraftingLevel { get; }

        /// <summary>The station whose payload is currently held, or null when none was opened.</summary>
        CraftingStation OpenedStation { get; }

        /// <summary>
        /// The offers the server last sent for <see cref="OpenedStation"/>. A snapshot: it changes
        /// only when the server says so, not when the bag or the registry changes underneath it.
        /// </summary>
        IReadOnlyList<RecipeOffer> AvailableRecipes { get; }

        /// <summary>
        /// The player's craft orders as the server last described them. The countdown in each row
        /// is the server's <see cref="CraftOrderView.RemainingSeconds"/>; the panel never compares
        /// its own clock to a deadline.
        /// </summary>
        IReadOnlyList<CraftOrderView> CraftOrders { get; }

        /// <summary>An explanation the server sent with the last payload (a full queue, for instance).</summary>
        string CraftMessage { get; }

        /// <summary>Raised when something other than a station open invalidates what the panel shows — a level change, for instance.</summary>
        event Action RecipesChanged;

        /// <summary>Raised by the server's answer to an open request: the station, and the offers it carries.</summary>
        event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

        /// <summary>Raised when the server pushes a new order payload.</summary>
        event Action OrdersChanged;

        /// <summary>An intent to craft <paramref name="recipeName"/> at <paramref name="station"/>; the server decides whether it happens.</summary>
        void RequestCraft(CraftingStation station, string recipeName);

        /// <summary>An intent to claim <paramref name="orderId"/>'s output or escrow; the server decides.</summary>
        void RequestClaimOrder(string orderId);

        /// <summary>An intent to cancel <paramref name="orderId"/> before it is due; the server decides.</summary>
        void RequestCancelOrder(string orderId);
    }
}
