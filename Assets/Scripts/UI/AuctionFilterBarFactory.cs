using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// Builds the auction browse filter strip in code: text/category/sort/seller/tier/price
    /// controls on two rows and a chip row for saved searches. Add it under an auction window and
    /// hand it to <see cref="AuctionHouseView.SetFilterBar"/>; the view owns search execution.
    /// </summary>
    public static class AuctionFilterBarFactory
    {
        public const float BarWidth = 760f;
        public const float BarHeight = 120f;

        private static readonly Color FieldColor = new Color(0.12f, 0.12f, 0.14f, 0.95f);
        private static readonly Color PlaceholderColor = new Color(0.65f, 0.65f, 0.7f, 1f);
        private static readonly Color ButtonColor = new Color(0.3f, 0.3f, 0.3f, 0.95f);

        public static AuctionFilterBar Create(Transform parent)
        {
            GameObject root = CreateRect(
                parent, AuctionFilterBar.GameObjectName, new Vector2(0f, 1f), new Vector2(0f, 1f),
                Vector2.zero, new Vector2(BarWidth, BarHeight));

            InputField text = CreateField(root.transform, "Text", 0f, 0f, 180f, "Search items");
            Button category = CreateButton(root.transform, "Category", 190f, 0f, 140f, "All categories");
            Button sort = CreateButton(root.transform, "Sort", 340f, 0f, 120f, "Relevance");
            Button save = CreateButton(root.transform, "Save", 470f, 0f, 80f, "Save");
            Button search = CreateButton(root.transform, "Search", 560f, 0f, 100f, "Search");

            InputField seller = CreateField(root.transform, "Seller", 0f, -30f, 140f, "Seller");
            InputField minTier = CreateField(root.transform, "MinTier", 150f, -30f, 70f, "Min tier");
            InputField maxTier = CreateField(root.transform, "MaxTier", 230f, -30f, 70f, "Max tier");
            InputField minPrice = CreateField(root.transform, "MinPrice", 310f, -30f, 90f, "Min gold");
            InputField maxPrice = CreateField(root.transform, "MaxPrice", 410f, -30f, 90f, "Max gold");

            GameObject chipsObject = CreateRect(
                root.transform, "SavedChips", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(0f, -62f), new Vector2(BarWidth, 26f));
            HorizontalLayoutGroup chipsLayout = chipsObject.AddComponent<HorizontalLayoutGroup>();
            chipsLayout.spacing = 4f;
            chipsLayout.childControlWidth = false;
            chipsLayout.childControlHeight = false;
            chipsLayout.childForceExpandWidth = false;
            chipsLayout.childForceExpandHeight = false;

            AuctionFilterBar bar = root.AddComponent<AuctionFilterBar>();
            bar.SetHosts(
                text, seller, minTier, maxTier, minPrice, maxPrice,
                category, GetLabel(category), sort, GetLabel(sort), save, search,
                (RectTransform)chipsObject.transform);

            return bar;
        }

        private static GameObject CreateRect(
            Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rectObject;
        }

        private static Button CreateButton(
            Transform parent, string name, float x, float y, float width, string text)
        {
            GameObject buttonObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, 24f));

            Image background = buttonObject.AddComponent<Image>();
            background.color = ButtonColor;

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            var labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(buttonObject.transform, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text label = labelObject.GetComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;

            return button;
        }

        private static Text GetLabel(Button button)
            => button != null ? button.GetComponentInChildren<Text>() : null;

        private static InputField CreateField(
            Transform parent, string name, float x, float y, float width, string placeholder)
        {
            GameObject fieldObject = CreateRect(
                parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(x, y), new Vector2(width, 24f));

            Image background = fieldObject.AddComponent<Image>();
            background.color = FieldColor;

            GameObject textObject = CreateStretch(fieldObject.transform, "Text");
            Text text = textObject.AddComponent<Text>();
            text.font = UIFont.Default;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.supportRichText = false;

            GameObject placeholderObject = CreateStretch(fieldObject.transform, "Placeholder");
            Text placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = UIFont.Default;
            placeholderText.fontSize = 12;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = PlaceholderColor;
            placeholderText.supportRichText = false;
            placeholderText.fontStyle = FontStyle.Italic;
            placeholderText.text = placeholder;

            InputField field = fieldObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholderText;
            field.targetGraphic = background;
            field.lineType = InputField.LineType.SingleLine;
            field.contentType = InputField.ContentType.Standard;
            return field;
        }

        private static GameObject CreateStretch(Transform parent, string name)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);
            return rectObject;
        }
    }
}
