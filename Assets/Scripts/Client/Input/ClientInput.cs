using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MMO.Client
{
    /// <summary>
    /// The owner-side input endpoint: one runtime action map, persisted rebinds, and the local
    /// settings that drive presentation.
    ///
    /// <para>
    /// Gameplay code asks <see cref="WasPressed"/> instead of reading a scancode, so a rebind
    /// changes every consumer at once. While the player is choosing a key, or the settings panel
    /// is open, gameplay actions are blocked; only <see cref="GameAction.OpenSettings"/> still
    /// reads through so the panel can be closed with the same key.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClientInput : MonoBehaviour
    {
        /// <summary>The owner's input endpoint, or null before the local HUD starts.</summary>
        public static ClientInput Current { get; private set; }

        private InputActionAsset _asset;
        private InputActionMap _map;
        private InputBindingStore _bindingStore;
        private InputRebindService _rebind;
        private SettingsStore _settingsStore;
        private SettingsModel _settings = SettingsModel.Defaults();

        /// <summary>True while a key is being captured.</summary>
        public bool IsRebinding => _rebind != null && _rebind.IsRebinding;

        /// <summary>When true, gameplay actions are ignored (the settings panel is open).</summary>
        public bool GameplayBlocked { get; private set; }

        /// <summary>The settings currently applied to this client.</summary>
        public SettingsModel Settings => _settings;

        /// <summary>Raised after a settings value was applied and saved.</summary>
        public event Action<SettingsModel> SettingsChanged;

        /// <summary>Raised after a successful rebind or a conflict cleared another binding.</summary>
        public event Action BindingsChanged;

        /// <summary>Ensures this client has an input endpoint; idempotent.</summary>
        public static ClientInput Ensure(Transform parent)
        {
            if (Current != null)
                return Current;

            var root = new GameObject("ClientInput");
            if (parent != null)
                root.transform.SetParent(parent, false);

            return root.AddComponent<ClientInput>();
        }

        /// <summary>True when <paramref name="action"/> was pressed this frame and gameplay is allowed.</summary>
        public bool WasPressed(GameAction action)
        {
            if (IsRebinding)
                return false;

            if (GameplayBlocked && action != GameAction.OpenSettings)
                return false;

            if (IsTypingInUi())
                return false;

            InputAction input = _map != null ? _map.FindAction(action.ToString()) : null;
            return input != null && input.WasPressedThisFrame();
        }

        /// <summary>Human-readable current binding, e.g. "G" or "Unbound".</summary>
        public string BindingDisplay(GameAction action)
        {
            InputAction input = _map != null ? _map.FindAction(action.ToString()) : null;
            if (input == null || input.bindings.Count == 0)
                return "Unbound";

            return input.GetBindingDisplayString(0);
        }

        /// <summary>Starts rebinding <paramref name="action"/>; false when another rebind is running.</summary>
        public bool BeginRebind(GameAction action, Action<bool> completed)
        {
            if (_rebind == null)
            {
                completed?.Invoke(false);
                return false;
            }

            bool started = _rebind.Begin(action.ToString(), success =>
            {
                if (success)
                    BindingsChanged?.Invoke();

                completed?.Invoke(success);
            });

            if (!started)
                completed?.Invoke(false);

            return started;
        }

        /// <summary>Applies and persists a settings draft, then refreshes presentation.</summary>
        public void SaveSettings(SettingsModel settings)
        {
            _settings = SettingsApplier.Apply(settings);
            _settingsStore?.Save(_settings);
            ClientEffects.Instance?.ApplySettings(_settings);
            SettingsChanged?.Invoke(_settings);
        }

        /// <summary>Blocks gameplay while a modal screen owns the keyboard.</summary>
        public void SetGameplayBlocked(bool blocked) => GameplayBlocked = blocked;

        private void Awake()
        {
            if (Current != null && Current != this)
            {
                Destroy(gameObject);
                return;
            }

            Current = this;

            _asset = GameInputActions.Create();
            _map = _asset.FindActionMap(GameInputActions.MapName);
            _bindingStore = new InputBindingStore();
            _bindingStore.LoadInto(_asset);
            _asset.Enable();

            _settingsStore = new SettingsStore();
            _settings = _settingsStore.Load();
            SettingsApplier.Apply(_settings);
            ClientEffects.Instance?.ApplySettings(_settings);

            _rebind = new InputRebindService(_asset, _bindingStore);
        }

        private void OnDestroy()
        {
            _rebind?.Cancel();

            if (_asset != null)
            {
                _asset.Disable();
                Destroy(_asset);
                _asset = null;
            }

            if (Current == this)
                Current = null;
        }

        private static bool IsTypingInUi()
        {
            EventSystem system = EventSystem.current;
            GameObject selected = system != null ? system.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<InputField>() != null;
        }
    }
}
