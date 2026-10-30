using MMO.Editor;
using UnityEditor;

namespace MMO.Editor
{
    public static class BakeAll
    {
        public const string MenuPath = "Tools/MMO/Bake All (Prefabs + Scenes)";

        [MenuItem(MenuPath)]
        public static void Execute()
        {
            // Bake prefabs first. Rebaking prefabs changes their internal fileIDs,
            // so any scenes that reference them must be rebaked afterwards.
            PlayerPrefabBaker.EnsurePlayerPrefab();
            TargetDummyBaker.EnsureTargetDummyPrefab();

            // Rebuild the crafting station prefab so it includes the NavMeshObstacle
            // with correct carving dimensions (fixes walk-through issue).
            CraftingStationBaker.EnsureCraftingStationPrefab();
            LootBagBaker.EnsureLootBagPrefab();
            ResourceNodeAssetBaker.Rebuild();
            ItemIconBaker.Rebuild();
            BankTellerBaker.EnsureBankTellerPrefab();

            // Rebuild scenes in an order that matches the expected startup flow.
            // MainMenu references the prefabs above and must be camera-free.
            MainMenuSceneBaker.CreateMainMenuScene();

            // Zone scenes get camera rigs injected by ZoneSceneBaker and also
            // reference shared prefabs; rebake them after prefabs.
            ZoneSceneBaker.BuildBoth();
        }
    }
}
