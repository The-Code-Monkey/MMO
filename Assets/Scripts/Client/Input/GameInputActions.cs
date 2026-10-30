using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    /// <summary>
    /// Builds the one gameplay action map at runtime. There is deliberately no committed
    /// <c>.inputactions</c> asset: the defaults live here, in code, so a fresh process and a test
    /// get the same map, and persisted overrides are the only thing stored per player.
    /// </summary>
    public static class GameInputActions
    {
        public const string MapName = "Gameplay";

        /// <summary>Creates the gameplay map with the shipped default bindings.</summary>
        public static InputActionAsset Create()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "GameplayControls";

            InputActionMap map = asset.AddActionMap(MapName);

            AddButton(map, GameAction.Ability1, "<Keyboard>/1");
            AddButton(map, GameAction.Ability2, "<Keyboard>/2");
            AddButton(map, GameAction.Ability3, "<Keyboard>/3");
            AddButton(map, GameAction.Ability4, "<Keyboard>/4");
            AddButton(map, GameAction.Ability5, "<Keyboard>/5");
            AddButton(map, GameAction.Ability6, "<Keyboard>/6");
            AddButton(map, GameAction.Mount, "<Keyboard>/g");
            AddButton(map, GameAction.Interact, "<Keyboard>/e");
            AddButton(map, GameAction.Trade, "<Keyboard>/t");
            AddButton(map, GameAction.Auction, "<Keyboard>/b");
            AddButton(map, GameAction.OpenSettings, "<Keyboard>/f10");

            return asset;
        }

        private static void AddButton(InputActionMap map, GameAction action, string path)
        {
            map.AddAction(action.ToString(), InputActionType.Button, path);
        }
    }
}
