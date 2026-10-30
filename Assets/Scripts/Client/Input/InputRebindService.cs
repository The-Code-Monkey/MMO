using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    /// <summary>
    /// One interactive rebind at a time, resolved through <see cref="InputBindingRules"/> before
    /// the new control is kept.
    ///
    /// While an operation runs every gameplay action is treated as blocked by the owning
    /// <see cref="ClientInput"/>, so pressing "1" to rebind Ability 1 cannot also fire Ability 1.
    /// </summary>
    public sealed class InputRebindService
    {
        private readonly InputActionAsset _asset;
        private readonly InputBindingStore _store;

        private InputActionRebindingExtensions.RebindingOperation _operation;
        private Action<bool> _completed;

        public InputRebindService(InputActionAsset asset, InputBindingStore store)
        {
            _asset = asset;
            _store = store;
        }

        /// <summary>True while the player is choosing a key.</summary>
        public bool IsRebinding => _operation != null;

        /// <summary>
        /// Starts rebinding <paramref name="actionName"/>. Returns false when another rebind is
        /// already running or the action does not exist.
        /// </summary>
        public bool Begin(string actionName, Action<bool> completed)
        {
            if (_operation != null || _asset == null)
                return false;

            InputAction action = _asset.FindAction(actionName);
            if (action == null)
                return false;

            _completed = completed;
            _operation = action
                .PerformInteractiveRebinding(bindingIndex: 0)
                .WithControlsExcluding("<Mouse>/position")
                .WithControlsExcluding("<Mouse>/delta")
                .WithCancelingThrough("<Keyboard>/escape")
                .OnMatchWaitForAnother(0.1f)
                .OnComplete(OnComplete)
                .OnCancel(OnCancel)
                .Start();

            return true;
        }

        /// <summary>Disposes a running operation, e.g. when the player object is destroyed.</summary>
        public void Cancel()
        {
            InputActionRebindingExtensions.RebindingOperation operation = _operation;
            _operation = null;
            _completed = null;
            operation?.Dispose();
        }

        private void OnComplete(InputActionRebindingExtensions.RebindingOperation operation)
            => Finish(matched: true);

        private void OnCancel(InputActionRebindingExtensions.RebindingOperation operation)
            => Finish(matched: false);

        private void Finish(bool matched)
        {
            InputActionRebindingExtensions.RebindingOperation operation = _operation;
            if (operation == null)
                return;

            _operation = null;
            InputAction action = operation.action;
            bool success = false;

            try
            {
                success = matched && !operation.canceled && action != null && action.bindings.Count > 0;

                if (success)
                {
                    string controlPath = action.bindings[0].effectivePath;
                    InputBindingPlan plan = InputBindingRules.Resolve(action.name, controlPath, Snapshot());

                    if (plan.IsReserved)
                    {
                        // The rebind operation already wrote the override; restore the default.
                        action.RemoveBindingOverride(0);
                        success = false;
                    }
                    else
                    {
                        ClearConflicts(action, plan);
                        _store?.Save(_asset);
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[InputRebindService] Rebind failed; keeping the previous binding. {exception.Message}");
                success = false;
            }
            finally
            {
                operation.Dispose();
            }

            Action<bool> completed = _completed;
            _completed = null;
            completed?.Invoke(success);
        }

        private void ClearConflicts(InputAction rebound, InputBindingPlan plan)
        {
            InputActionMap map = _asset.FindActionMap(GameInputActions.MapName);
            if (map == null)
                return;

            for (int i = 0; i < plan.ClearedActions.Count; i++)
            {
                InputAction conflict = map.FindAction(plan.ClearedActions[i]);
                if (conflict == null || conflict == rebound)
                    continue;

                // Erase from the end so earlier indices stay valid.
                for (int binding = conflict.bindings.Count - 1; binding >= 0; binding--)
                    conflict.ChangeBinding(binding).Erase();
            }
        }

        private Dictionary<string, string> Snapshot()
        {
            var current = new Dictionary<string, string>(StringComparer.Ordinal);
            InputActionMap map = _asset.FindActionMap(GameInputActions.MapName);
            if (map == null)
                return current;

            foreach (InputAction action in map.actions)
            {
                if (action.bindings.Count > 0)
                    current[action.name] = action.bindings[0].effectivePath;
            }

            return current;
        }
    }
}
