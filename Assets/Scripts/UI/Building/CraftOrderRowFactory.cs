using System;
using MMO.Crafting;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds one craft-order row: what is being made, its server-sent status, and its claim.</summary>
    internal static class CraftOrderRowFactory
    {
        public static CraftingPanelView.OrderRow Create(
            CraftOrderView order, Transform parent, int index, Action<int> onAction)
        {
            var rowObject = new GameObject(
                $"Order {order.RecipeName}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(0f, CraftingPanelView.RowHeight);

            LayoutElement layout = rowObject.GetComponent<LayoutElement>();
            layout.minHeight = CraftingPanelView.RowHeight;
            layout.preferredHeight = CraftingPanelView.RowHeight;

            Image background = rowObject.GetComponent<Image>();
            background.color = CraftingPanelView.OrderRowColor;

            Text nameLabel = CreateLabel(rowObject.transform, "Name", TextAnchor.MiddleLeft);
            nameLabel.text = order.OutputCount > 1
                ? $"{order.RecipeName} x{order.OutputCount}"
                : order.RecipeName;

            Text statusLabel = CreateLabel(rowObject.transform, "Status", TextAnchor.MiddleRight);
            statusLabel.text = CraftOrderUiFormat.Status(order);

            Button actionButton = CreateButton(
                rowObject.transform, "OrderAction", CraftOrderUiFormat.ActionLabel(order));
            actionButton.interactable = CraftOrderUiFormat.CanClaim(order);
            actionButton.onClick.AddListener(() => onAction(index));

            return new CraftingPanelView.OrderRow(order, rowObject, nameLabel, statusLabel, actionButton);
        }

        private static Text CreateLabel(Transform parent, string name, TextAnchor alignment)
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.offsetMin = new Vector2(10f, 0f);
            rect.offsetMax = new Vector2(-140f, 0f);

            Text label = labelObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 13;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = 8;
            label.resizeTextMaxSize = 13;
            label.text = string.Empty;
            return label;
        }

        private static Button CreateButton(Transform parent, string name, string text)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-6f, 0f);
            rect.sizeDelta = new Vector2(64f, CraftingPanelView.RowHeight - 8f);

            Image background = buttonObject.GetComponent<Image>();
            background.color = new Color(0.2f, 0.32f, 0.2f, 0.95f);

            Button button = buttonObject.AddComponent<Button>();
            button.targetGraphic = background;

            var textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(buttonObject.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text label = textObject.AddComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 12;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = text;

            return button;
        }
    }
}
