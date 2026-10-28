using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Primitive uGUI construction helpers shared by the code-built screens.</summary>
    public static class UIBuilder
    {
        public static GameObject Rect(
            Transform parent,
            string name,
            Vector2 anchor,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return rectObject;
        }

        public static GameObject Stretch(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rectObject;
        }

        /// <summary>
        /// A horizontally stretched, vertically fixed row. The background is clear, so callers
        /// that only need a layout anchor show nothing and callers that need a bar assign a colour.
        /// </summary>
        public static GameObject Row(
            Transform parent,
            string name,
            float anchorY,
            Vector2 pivot,
            Vector2 anchoredPosition,
            Vector2 sizeDelta)
        {
            var rowObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, anchorY);
            rect.anchorMax = new Vector2(1f, anchorY);
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Image background = rowObject.GetComponent<Image>();
            background.color = Color.clear;
            background.raycastTarget = false;

            return rowObject;
        }

        public static Text Label(Transform parent)
        {
            var labelObject = new GameObject("Label", typeof(RectTransform));
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return labelObject.AddComponent<Text>();
        }

        public static GameObject Inset(Transform parent, string name, Vector2 inset)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = inset;
            rect.offsetMax = -inset;
            return rectObject;
        }

        public static void ApplyLabel(
            Text label, int fontSize, TextAnchor alignment, int resizeTextMinSize = 7)
        {
            if (label == null)
                return;

            label.font = UIFont.Default;
            label.fontSize = fontSize;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = resizeTextMinSize;
            label.resizeTextMaxSize = fontSize;
            label.text = string.Empty;
        }
    }
}
