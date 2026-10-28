using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>Builds the code-driven chat and friend panel widget tree.</summary>
    internal static class ChatFeedFactory
    {
        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.08f);

        /// <summary>Builds the chat box and friend panel under <paramref name="parent"/>.</summary>
        public static ChatFeed Create(Transform parent)
        {
            GameObject root = new GameObject(ChatFeed.GameObjectName, typeof(RectTransform));
            RectTransform rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            ChatFeed feed = root.AddComponent<ChatFeed>();

            // Chat box: bottom-left.
            GameObject chatPanel = CreatePanel(
                root.transform, "ChatBox", new Vector2(0f, 0f), new Vector2(0f, 0f),
                new Vector2(12f, 12f), new Vector2(560f, 200f));
            feed._chatLog = CreateText(chatPanel.transform, "Log", 13, TextAnchor.LowerLeft);
            feed._chatLog.rectTransform.offsetMin = new Vector2(8f, 70f);
            feed._chatLog.rectTransform.offsetMax = new Vector2(-8f, -8f);
            CreateRow(chatPanel.transform,
                out feed._chatInput, out Button sendButton, "Say something…", "Send");
            sendButton.onClick.AddListener(feed.SendChat);

            GameObject channelRow = CreateRowParent(chatPanel.transform, "Channels", 26f, 36f);
            feed._channelLabel = CreateText(channelRow.transform, "Current", 12, TextAnchor.MiddleLeft);
            feed.SetChannel(ChatRules.ChannelGlobal);
            feed._channelLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            feed._channelLabel.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            feed._channelLabel.rectTransform.offsetMin = Vector2.zero;
            feed._channelLabel.rectTransform.offsetMax = Vector2.zero;

            GameObject globalButton = CreateButton(channelRow.transform, "Global", out Button globalBtn);
            globalBtn.onClick.AddListener(() => feed.SetChannel(ChatRules.ChannelGlobal));
            globalButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0f);
            globalButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.67f, 1f);

            GameObject zoneButton = CreateButton(channelRow.transform, "Zone", out Button zoneBtn);
            zoneBtn.onClick.AddListener(() => feed.SetChannel(ChatRules.ChannelZone));
            zoneButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.67f, 0f);
            zoneButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.84f, 1f);

            GameObject partyButton = CreateButton(channelRow.transform, "Party", out Button partyBtn);
            partyBtn.onClick.AddListener(() => feed.SetChannel(ChatRules.ChannelParty));
            partyButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.84f, 0f);
            partyButton.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);

            // Friend panel: top-right.
            GameObject friendPanel = CreatePanel(
                root.transform, "FriendList", new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-12f, -12f), new Vector2(260f, 240f));
            feed._friendLog = CreateText(friendPanel.transform, "Friends", 13, TextAnchor.UpperLeft);
            feed._friendLog.rectTransform.offsetMin = new Vector2(8f, 70f);
            feed._friendLog.rectTransform.offsetMax = new Vector2(-8f, -8f);
            CreateRow(friendPanel.transform,
                out feed._friendInput, out Button addButton, "Add friend…", "Add");
            addButton.onClick.AddListener(feed.AddFriend);

            GameObject actionRow = CreateRowParent(friendPanel.transform, "Actions", 28f, 36f);
            GameObject acceptButton = CreateButton(actionRow.transform, "Accept", out Button accept);
            accept.onClick.AddListener(feed.AcceptFirstPending);
            acceptButton.GetComponent<RectTransform>().anchorMin = new Vector2(0f, 0f);
            acceptButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);

            GameObject removeButton = CreateButton(actionRow.transform, "Remove", out Button remove);
            remove.onClick.AddListener(feed.RemoveFirstFriend);
            removeButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0f);
            removeButton.GetComponent<RectTransform>().anchorMax = new Vector2(1f, 1f);

            return feed;
        }

        private static GameObject CreatePanel(
            Transform parent, string name, Vector2 anchor, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            GameObject panel = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)panel.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            panel.AddComponent<Image>().color = PanelColor;
            return panel;
        }

        private static Text CreateText(Transform parent, string name, int fontSize, TextAnchor anchor)
        {
            GameObject obj = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 8f);
            rect.offsetMax = new Vector2(-8f, -8f);

            Text text = obj.AddComponent<Text>();
            text.font = UIFont.Default;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.text = string.Empty;
            return text;
        }

        private static GameObject CreateRowParent(Transform parent, string name, float height, float yOffset)
        {
            GameObject row = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)row.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, yOffset);
            rect.sizeDelta = new Vector2(-16f, height);
            return row;
        }

        private static void CreateRow(
            Transform parent, out InputField input, out Button button, string placeholder, string buttonLabel)
        {
            GameObject row = CreateRowParent(parent, "InputRow", 28f, 4f);

            GameObject inputObject = new GameObject("Input", typeof(RectTransform));
            RectTransform inputRect = (RectTransform)inputObject.transform;
            inputRect.SetParent(row.transform, false);
            inputRect.anchorMin = new Vector2(0f, 0f);
            inputRect.anchorMax = new Vector2(0.76f, 1f);
            inputRect.offsetMin = Vector2.zero;
            inputRect.offsetMax = Vector2.zero;

            Image inputBackground = inputObject.AddComponent<Image>();
            inputBackground.color = RowColor;

            input = inputObject.AddComponent<InputField>();
            input.targetGraphic = inputBackground;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(inputObject.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(6f, 0f);
            textRect.offsetMax = new Vector2(-6f, 0f);

            Text text = textObject.AddComponent<Text>();
            text.font = UIFont.Default;
            text.fontSize = 13;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.supportRichText = false;
            input.textComponent = text;

            GameObject placeholderObject = new GameObject("Placeholder", typeof(RectTransform));
            RectTransform placeholderRect = (RectTransform)placeholderObject.transform;
            placeholderRect.SetParent(inputObject.transform, false);
            placeholderRect.anchorMin = Vector2.zero;
            placeholderRect.anchorMax = Vector2.one;
            placeholderRect.offsetMin = new Vector2(6f, 0f);
            placeholderRect.offsetMax = new Vector2(-6f, 0f);

            Text placeholderText = placeholderObject.AddComponent<Text>();
            placeholderText.font = UIFont.Default;
            placeholderText.fontSize = 13;
            placeholderText.alignment = TextAnchor.MiddleLeft;
            placeholderText.color = new Color(1f, 1f, 1f, 0.4f);
            placeholderText.text = placeholder;
            placeholderText.raycastTarget = false;
            input.placeholder = placeholderText;

            GameObject buttonObject = CreateButton(row.transform, buttonLabel, out button);
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.78f, 0f);
            buttonRect.anchorMax = new Vector2(1f, 1f);
        }

        private static GameObject CreateButton(Transform parent, string label, out Button button)
        {
            GameObject obj = new GameObject(label + "Button", typeof(RectTransform));
            RectTransform rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(2f, 0f);
            rect.offsetMax = new Vector2(-2f, 0f);

            Image background = obj.AddComponent<Image>();
            background.color = RowColor;

            button = obj.AddComponent<Button>();
            button.targetGraphic = background;

            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(obj.transform, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textObject.AddComponent<Text>();
            text.font = UIFont.Default;
            text.fontSize = 12;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            text.text = label;

            return obj;
        }
    }
}
