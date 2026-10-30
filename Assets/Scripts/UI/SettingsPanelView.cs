using System;
using System.Collections.Generic;
using MMO.Client;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Widgets the settings factory builds and the view drives.</summary>
    public sealed class SettingsPanelWidgets
    {
        public Text QualityLabel;
        public Slider MasterVolume;
        public Slider MusicVolume;
        public Slider SfxVolume;
        public Slider Interpolation;
        public Text MasterVolumeValue;
        public Text MusicVolumeValue;
        public Text SfxVolumeValue;
        public Text InterpolationValue;
        public Text AllyEffectsLabel;
        public Text Status;

        public readonly Dictionary<GameAction, Text> BindingLabels = new();
        public readonly Dictionary<GameAction, Button> BindingButtons = new();
    }

    /// <summary>
    /// The settings window: quality, volumes, the ZvZ ally-effect toggle, interpolation and one
    /// rebind row per action.
    ///
    /// Every change is applied and saved immediately, so lowering quality during a fight does
    /// not need an Apply button. The view never polls; it repaints when <see cref="ClientInput"/>
    /// says settings or bindings changed.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Settings Panel View")]
    public sealed class SettingsPanelView : MonoBehaviour
    {
        private ClientInput _input;
        private SettingsPanelWidgets _widgets;
        private bool _suppress;

        /// <summary>True while the panel is on screen.</summary>
        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Binds the panel to the client's settings and input map.</summary>
        public void Bind(ClientInput input, SettingsPanelWidgets widgets)
        {
            Unbind();

            _input = input;
            _widgets = widgets;

            if (_input != null)
            {
                _input.SettingsChanged += OnSettingsChanged;
                _input.BindingsChanged += RefreshBindings;
            }

            Refresh();
        }

        /// <summary>Shows the panel and blocks gameplay keys behind it.</summary>
        public void Reveal()
        {
            gameObject.SetActive(true);
            _input?.SetGameplayBlocked(true);
            Refresh();
        }

        /// <summary>Hides the panel and releases gameplay keys.</summary>
        public void Hide()
        {
            gameObject.SetActive(false);
            _input?.SetGameplayBlocked(false);

            if (_widgets?.Status != null)
                _widgets.Status.text = string.Empty;
        }

        /// <summary>Shows a hidden panel or hides a visible one.</summary>
        public void Toggle()
        {
            if (IsOpen)
                Hide();
            else
                Reveal();
        }

        public void SetQualityDelta(int delta)
        {
            if (_input == null)
                return;

            SettingsModel settings = _input.Settings.Clone();
            settings.QualityLevel += delta;
            _input.SaveSettings(settings);
        }

        public void SetMasterVolume(float value) => Save(settings => settings.MasterVolume = value);

        public void SetMusicVolume(float value) => Save(settings => settings.MusicVolume = value);

        public void SetSfxVolume(float value) => Save(settings => settings.SfxVolume = value);

        public void SetInterpolation(float value)
            => Save(settings => settings.NetworkInterpolationMs = Mathf.RoundToInt(value));

        public void ToggleAllyEffects()
            => Save(settings => settings.ShowAllySpellEffects = !settings.ShowAllySpellEffects);

        /// <summary>Starts a rebind and reports the outcome in the status line.</summary>
        public void BeginRebind(GameAction action)
        {
            if (_input == null)
                return;

            SetStatus($"Press a key for {GameActions.DisplayName(action)}...");
            _input.BeginRebind(action, success =>
            {
                SetStatus(success
                    ? $"{GameActions.DisplayName(action)} bound to {_input.BindingDisplay(action)}."
                    : "Rebind cancelled or reserved.");

                RefreshBindings();
            });
        }

        /// <summary>Repaints every control from the current settings and bindings.</summary>
        public void Refresh()
        {
            if (_input == null || _widgets == null)
                return;

            SettingsModel settings = _input.Settings;
            _suppress = true;

            if (_widgets.QualityLabel != null)
                _widgets.QualityLabel.text = QualityName(settings.QualityLevel);

            SetSlider(_widgets.MasterVolume, settings.MasterVolume);
            SetSlider(_widgets.MusicVolume, settings.MusicVolume);
            SetSlider(_widgets.SfxVolume, settings.SfxVolume);
            SetSlider(_widgets.Interpolation, settings.NetworkInterpolationMs);

            SetLabel(_widgets.MasterVolumeValue, Percent(settings.MasterVolume));
            SetLabel(_widgets.MusicVolumeValue, Percent(settings.MusicVolume));
            SetLabel(_widgets.SfxVolumeValue, Percent(settings.SfxVolume));
            SetLabel(_widgets.InterpolationValue, $"{settings.NetworkInterpolationMs} ms");
            SetLabel(_widgets.AllyEffectsLabel, settings.ShowAllySpellEffects ? "Ally effects: On" : "Ally effects: Off");

            _suppress = false;
            RefreshBindings();
        }

        /// <summary>Repaints just the rebind rows.</summary>
        public void RefreshBindings()
        {
            if (_input == null || _widgets == null)
                return;

            foreach (KeyValuePair<GameAction, Text> row in _widgets.BindingLabels)
            {
                if (row.Value != null)
                    row.Value.text = _input.BindingDisplay(row.Key);
            }

            foreach (KeyValuePair<GameAction, Button> row in _widgets.BindingButtons)
            {
                if (row.Value != null)
                    row.Value.interactable = !_input.IsRebinding;
            }
        }

        private void Save(Action<SettingsModel> change)
        {
            if (_input == null || _suppress)
                return;

            SettingsModel settings = _input.Settings.Clone();
            change(settings);
            _input.SaveSettings(settings);
        }

        private void OnSettingsChanged(SettingsModel settings) => Refresh();

        private void SetStatus(string message)
        {
            if (_widgets?.Status != null)
                _widgets.Status.text = message;
        }

        private static void SetSlider(Slider slider, float value)
        {
            if (slider != null)
                slider.SetValueWithoutNotify(value);
        }

        private static void SetLabel(Text label, string value)
        {
            if (label != null)
                label.text = value;
        }

        private static string Percent(float value) => $"{Mathf.RoundToInt(value * 100f)}%";

        private static string QualityName(int level)
        {
            string[] names = QualitySettings.names;
            if (names == null || names.Length == 0)
                return level.ToString();

            int index = Mathf.Clamp(level, 0, names.Length - 1);
            return names[index];
        }

        private void Unbind()
        {
            if (_input != null)
            {
                _input.SettingsChanged -= OnSettingsChanged;
                _input.BindingsChanged -= RefreshBindings;
            }

            _input = null;
        }

        private void OnDestroy() => Unbind();
    }
}
