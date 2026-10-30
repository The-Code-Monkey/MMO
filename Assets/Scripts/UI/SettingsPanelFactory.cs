using System;
using System.Collections.Generic;
using MMO.Client;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the settings window in code, matching every other screen in this project: no
    /// prefab, no scene asset, one factory the game and a test can call.
    /// </summary>
    public static class SettingsPanelFactory
    {
        public const string PanelName = "SettingsPanel";
        public const float PanelWidth = 540f;
        public const float PanelHeight = 820f;

        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.82f);
        private static readonly Color ButtonColor = new(0.14f, 0.14f, 0.17f, 0.95f);
        private static readonly Color SliderColor = new(0.12f, 0.12f, 0.15f, 0.95f);
        private static readonly Color FillColor = new(0.28f, 0.45f, 0.82f, 0.95f);

        public static SettingsPanelView Create(Transform parent, ClientInput input)
        {
            GameObject panel = UIBuilder.Rect(
                parent, PanelName, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(PanelWidth, PanelHeight));
            panel.AddComponent<Image>().color = PanelColor;

            SettingsPanelView view = panel.AddComponent<SettingsPanelView>();
            var widgets = new SettingsPanelWidgets();

            Text title = Label(panel.transform, "Title", 16f, -10f, 360f, 32f, 22, TextAnchor.MiddleLeft);
            title.text = "Settings";

            Button close = Button(panel.transform, "Close", 468f, -12f, 56f, 28f, "Close", 13);
            close.onClick.AddListener(view.Hide);

            Label(panel.transform, "QualityName", 16f, -54f, 150f, 26f, 14, TextAnchor.MiddleLeft).text = "Quality";
            widgets.QualityLabel = Label(
                panel.transform, "QualityValue", 170f, -54f, 150f, 26f, 14, TextAnchor.MiddleLeft);
            Button qualityDown = Button(panel.transform, "QualityDown", 330f, -54f, 34f, 26f, "<", 14);
            Button qualityUp = Button(panel.transform, "QualityUp", 370f, -54f, 34f, 26f, ">", 14);
            qualityDown.onClick.AddListener(() => view.SetQualityDelta(-1));
            qualityUp.onClick.AddListener(() => view.SetQualityDelta(1));

            widgets.MasterVolume = SliderRow(
                panel.transform, "MasterVolume", -94f, "Master", out widgets.MasterVolumeValue);
            widgets.MusicVolume = SliderRow(
                panel.transform, "MusicVolume", -134f, "Music", out widgets.MusicVolumeValue);
            widgets.SfxVolume = SliderRow(
                panel.transform, "SfxVolume", -174f, "SFX", out widgets.SfxVolumeValue);

            Button ally = Button(panel.transform, "AllyEffects", 16f, -214f, 240f, 28f, "Ally effects: On", 13);
            widgets.AllyEffectsLabel = ally.GetComponentInChildren<Text>();
            ally.onClick.AddListener(view.ToggleAllyEffects);

            widgets.Interpolation = SliderRow(
                panel.transform, "Interpolation", -254f, "Interpolation", out widgets.InterpolationValue);
            widgets.Interpolation.minValue = SettingsRules.MinInterpolationMs;
            widgets.Interpolation.maxValue = SettingsRules.MaxInterpolationMs;
            widgets.Interpolation.wholeNumbers = true;

            Text bindingsTitle = Label(
                panel.transform, "BindingsTitle", 16f, -294f, 300f, 26f, 16, TextAnchor.MiddleLeft);
            bindingsTitle.text = "Key bindings";

            float rowY = -326f;
            foreach (GameAction action in Enum.GetValues(typeof(GameAction)))
            {
                Text name = Label(
                    panel.transform, action + "Name", 16f, rowY, 180f, 26f, 13, TextAnchor.MiddleLeft);
                name.text = GameActions.DisplayName(action);

                Text binding = Label(
                    panel.transform, action + "Binding", 200f, rowY, 130f, 26f, 13, TextAnchor.MiddleLeft);
                Button rebind = Button(
                    panel.transform, action + "Rebind", 340f, rowY, 120f, 26f, "Rebind", 12);
                GameAction captured = action;
                rebind.onClick.AddListener(() => view.BeginRebind(captured));

                widgets.BindingLabels[action] = binding;
                widgets.BindingButtons[action] = rebind;
                rowY -= 32f;
            }

            widgets.Status = Label(
                panel.transform, "Status", 16f, rowY - 8f, PanelWidth - 32f, 44f, 12, TextAnchor.UpperLeft);

            view.Bind(input, widgets);
            HookSlider(widgets.MasterVolume, view.SetMasterVolume);
            HookSlider(widgets.MusicVolume, view.SetMusicVolume);
            HookSlider(widgets.SfxVolume, view.SetSfxVolume);
            HookSlider(widgets.Interpolation, view.SetInterpolation);

            view.Hide();
            return view;
        }

        private static Slider SliderRow(
            Transform parent, string name, float y, string labelText, out Text valueLabel)
        {
            Label(parent, name + "Label", 16f, y, 150f, 26f, 14, TextAnchor.MiddleLeft).text = labelText;

            GameObject sliderObject = UIBuilder.Rect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(170f, y - 4f), new Vector2(220f, 22f));

            Image background = UIBuilder.Stretch(sliderObject.transform, "Background").AddComponent<Image>();
            background.color = SliderColor;

            GameObject fillArea = UIBuilder.Stretch(sliderObject.transform, "Fill Area");
            var fillAreaRect = (RectTransform)fillArea.transform;
            fillAreaRect.offsetMin = new Vector2(4f, 5f);
            fillAreaRect.offsetMax = new Vector2(-4f, -5f);

            Image fill = UIBuilder.Stretch(fillArea.transform, "Fill").AddComponent<Image>();
            fill.color = FillColor;

            GameObject handleArea = UIBuilder.Stretch(sliderObject.transform, "Handle Slide Area");
            var handleAreaRect = (RectTransform)handleArea.transform;
            handleAreaRect.offsetMin = new Vector2(8f, 0f);
            handleAreaRect.offsetMax = new Vector2(-8f, 0f);

            GameObject handle = UIBuilder.Rect(
                handleArea.transform, "Handle", new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f),
                Vector2.zero, new Vector2(16f, 22f));
            Image handleImage = handle.AddComponent<Image>();
            handleImage.color = Color.white;

            Slider slider = sliderObject.AddComponent<Slider>();
            slider.fillRect = (RectTransform)fill.transform;
            slider.handleRect = (RectTransform)handle.transform;
            slider.targetGraphic = handleImage;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;

            valueLabel = Label(parent, name + "Value", 400f, y, 110f, 26f, 12, TextAnchor.MiddleLeft);
            return slider;
        }

        private static void HookSlider(Slider slider, UnityEngine.Events.UnityAction<float> action)
        {
            if (slider != null)
                slider.onValueChanged.AddListener(action);
        }

        private static Text Label(
            Transform parent, string name, float x, float y, float width, float height,
            int fontSize, TextAnchor alignment)
        {
            GameObject labelObject = UIBuilder.Rect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, height));

            Text label = labelObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(label, fontSize, alignment, resizeTextMinSize: 8);
            return label;
        }

        private static Button Button(
            Transform parent, string name, float x, float y, float width, float height,
            string text, int fontSize)
        {
            GameObject buttonObject = UIBuilder.Rect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, height));

            Image image = buttonObject.AddComponent<Image>();
            image.color = ButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = image;

            Text label = UIBuilder.Label(buttonObject.transform);
            UIBuilder.ApplyLabel(label, fontSize, TextAnchor.MiddleCenter, resizeTextMinSize: 7);
            label.text = text;
            return button;
        }
    }
}
