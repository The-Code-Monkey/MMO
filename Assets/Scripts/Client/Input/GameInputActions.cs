using System;
using System.Security.Cryptography;
using System.Text;
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
            AddButton(map, GameAction.MoveForward, "<Keyboard>/w");
            AddButton(map, GameAction.MoveBack, "<Keyboard>/s");
            AddButton(map, GameAction.StrafeLeft, "<Keyboard>/q");
            AddButton(map, GameAction.StrafeRight, "<Keyboard>/e");
            AddButton(map, GameAction.Mount, "<Keyboard>/g");
            AddButton(map, GameAction.Interact, "<Keyboard>/e");
            AddButton(map, GameAction.Trade, "<Keyboard>/t");
            AddButton(map, GameAction.Auction, "<Keyboard>/b");
            AddButton(map, GameAction.Mail, "<Keyboard>/m");
            AddButton(map, GameAction.OpenSettings, "<Keyboard>/f10");

            return asset;
        }

        /// <summary>
        /// Adds one button action. The binding id is derived from the map, action and default path
        /// instead of being generated: persisted overrides are matched by binding id, so a
        /// code-built map has to hand out the same ids on every run or every rebind a player saved
        /// is silently dropped at startup.
        /// </summary>
        private static void AddButton(InputActionMap map, GameAction action, string path)
        {
            InputAction added = map.AddAction(action.ToString(), InputActionType.Button);
            added.AddBinding(new InputBinding
            {
                path = path,
                id = StableBindingId(added.name, path),
            });
        }

        /// <summary>The same id in every process that builds the same map.</summary>
        private static Guid StableBindingId(string action, string path)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(MapName + "/" + action + "/" + path));
                return new Guid(hash);
            }
        }
    }
}
