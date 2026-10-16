using System;
using System.Collections.Generic;
using MMO.Crafting;

namespace MMO.UI
{
    /// <summary>
    /// What a crafting panel reads: the player's crafting progress, the station that is open, the
    /// recipe offers the server sent for it, and the two intents the panel is allowed to express
    /// (craft a named recipe at that station).
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

        /// <summary>Raised when something other than a station open invalidates what the panel shows — a level change, for instance.</summary>
        event Action RecipesChanged;

        /// <summary>Raised by the server's answer to an open request: the station, and the offers it carries.</summary>
        event Action<CraftingStation, IReadOnlyList<RecipeOffer>> StationOpened;

        /// <summary>An intent to craft <paramref name="recipeName"/> at <paramref name="station"/>; the server decides whether it happens.</summary>
        void RequestCraft(CraftingStation station, string recipeName);
    }
}
