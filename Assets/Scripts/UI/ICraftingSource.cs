using System;
using MMO.Crafting;

namespace MMO.UI
{
    public interface ICraftingSource
    {
        int CraftingXp { get; }
        int CraftingLevel { get; }
        CraftingStation OpenedStation { get; }
        event Action RecipesChanged;
        event Action StationOpened;
    }
}
