using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds the player health/mana HUD panel.</summary>
    internal static class PlayerVitalsFactory
    {
        public static PlayerHudView CreateHud(Transform parent)
        {
            GameObject panel = UIBuilder.Rect(
                parent, "PlayerHud", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(12f, -12f), new Vector2(300f, 84f));
            panel.AddComponent<Image>().color = HudTheme.PanelColor;

            PlayerHudView hud = panel.AddComponent<PlayerHudView>();
            hud.SetBars(
                CreateVitalBar(panel.transform, "HealthBar", VitalKind.Health,
                    new Vector2(10f, -10f), HudTheme.HealthColor),
                CreateVitalBar(panel.transform, "ManaBar", VitalKind.Mana,
                    new Vector2(10f, -46f), HudTheme.ManaColor));

            return hud;
        }

        private static VitalBarView CreateVitalBar(
            Transform parent, string name, VitalKind kind, Vector2 position, Color fillColor)
        {
            GameObject barObject = UIBuilder.Rect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                position, new Vector2(280f, 28f));
            barObject.AddComponent<Image>().color = HudTheme.TroughColor;

            GameObject fillObject = UIBuilder.Stretch(barObject.transform, "Fill");
            Image fill = fillObject.AddComponent<Image>();
            fill.color = fillColor;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;

            GameObject valueObject = UIBuilder.Stretch(barObject.transform, "Value");
            Text value = valueObject.AddComponent<Text>();
            UIBuilder.ApplyLabel(value, 13, TextAnchor.MiddleCenter);

            VitalBarView bar = barObject.AddComponent<VitalBarView>();
            bar.SetKind(kind);
            bar.SetVisuals(fill, value);
            return bar;
        }
    }
}
