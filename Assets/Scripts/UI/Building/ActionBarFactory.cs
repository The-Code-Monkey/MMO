using MMO.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds the ability action bar.</summary>
    internal static class ActionBarFactory
    {
        public static ActionBarView Create(Transform parent)
        {
            GameObject panel = UIBuilder.Rect(
                parent, "ActionBar", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 16f),
                new Vector2(AbilityLoadout.MaxSlots * ActionBarView.SlotSize, ActionBarView.SlotSize));
            panel.AddComponent<Image>().color = HudTheme.PanelColor;

            ActionBarView bar = panel.AddComponent<ActionBarView>();
            bar.SetContainer(UIBuilder.Stretch(panel.transform, "Slots").GetComponent<RectTransform>());
            return bar;
        }
    }
}
