using MMO.Content;
using MMO.Crafting;
using MMO.Gameplay;
using MMO.Inventory;

namespace MMO.Net
{
    /// <summary>Loads every Resources-backed content registry the server needs at spawn time.</summary>
    public static class ZoneContentRegistryLoader
    {
        public static void LoadAll()
        {
            RecipeRegistry.LoadAllFromResources();

            // The tool gate resolves an item by name, so an unloaded item is a tool the server
            // cannot recognise.
            ItemDefinitionRegistry.LoadAllFromResources();

            // Mounts are summoned by item name.
            MountDataRegistry.LoadAllFromResources();

            // Content registries resolve persisted quest ids and loot tables by name.
            QuestRegistry.LoadAllFromResources();
            LootTableRegistry.LoadAllFromResources();
        }
    }
}
