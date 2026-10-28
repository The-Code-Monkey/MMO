using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds the target name/health frame.</summary>
    internal static class TargetFrameFactory
    {
        public static TargetFrameView Create(Transform parent)
        {
            GameObject frame = UIBuilder.Rect(
                parent, "TargetFrame", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -12f), new Vector2(260f, 64f));
            frame.AddComponent<Image>().color = HudTheme.PanelColor;

            GameObject nameRow = UIBuilder.Row(
                frame.transform, "Name", 1f, new Vector2(0.5f, 1f),
                new Vector2(0f, -4f), new Vector2(-20f, 26f));
            Text nameText = UIBuilder.Label(nameRow.transform);
            UIBuilder.ApplyLabel(nameText, 14, TextAnchor.MiddleCenter);

            GameObject fillRow = UIBuilder.Row(
                frame.transform, "HealthFill", 0f, new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(-20f, 26f));
            Image healthFill = fillRow.GetComponent<Image>();
            healthFill.color = HudTheme.HealthColor;
            healthFill.type = Image.Type.Filled;
            healthFill.fillMethod = Image.FillMethod.Horizontal;

            GameObject valueRow = UIBuilder.Row(
                frame.transform, "HealthValue", 0f, new Vector2(0.5f, 0f),
                new Vector2(0f, 6f), new Vector2(-20f, 26f));
            Text healthText = UIBuilder.Label(valueRow.transform);
            UIBuilder.ApplyLabel(healthText, 13, TextAnchor.MiddleCenter);

            TargetFrameView view = frame.AddComponent<TargetFrameView>();
            view.SetVisuals(frame, nameText, healthFill, healthText);
            return view;
        }
    }
}
