using UnityEngine;

namespace MMO.UI
{
    /// <summary>Shared colours and metrics for the code-built HUD.</summary>
    public static class HudTheme
    {
        public static readonly Color PanelColor = new(0f, 0f, 0f, 0.45f);
        public static readonly Color TroughColor = new(0f, 0f, 0f, 0.6f);
        public static readonly Color HealthColor = new(0.75f, 0.15f, 0.15f, 0.95f);
        public static readonly Color ManaColor = new(0.2f, 0.4f, 0.9f, 0.95f);
        public static readonly Color IconBackground = new(0f, 0f, 0f, 0.65f);
    }
}
