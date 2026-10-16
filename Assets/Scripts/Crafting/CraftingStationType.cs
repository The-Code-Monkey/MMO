using System;

namespace MMO.Crafting
{
    public enum CraftingStationType
    {
        None = 0,
        Furnace = 1,   // refining: ore -> bar
        Anvil = 2,     // crafting: bar -> gear
        Alchemy = 3,   // brewing: reagents -> potions
    }
}
