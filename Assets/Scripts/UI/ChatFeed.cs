using System.Collections.Generic;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The client's chat box and friend list, built entirely in code like the rest of the HUD.
    /// Delivery is push-only: <see cref="PlayerChat"/> and <see cref="PlayerFriends"/> call the
    /// static <c>Push*</c> methods with the server's broadcasts, and the panel repaints.
    ///
    /// <para>
    /// A client that never builds the panel loses nothing — every push is null-guarded, so the
    /// gameplay path stays identical whether or not a HUD exists.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Chat Feed")]
    public sealed class ChatFeed : MonoBehaviour
    {
        public const string GameObjectName = "ChatFeed";

        private const int MaxChatLines = 12;
        private const int MaxFriendLines = 16;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color RowColor = new Color(1f, 1f, 1f, 0.08f);

        public static ChatFeed Instance { get; private set; }

        private readonly List<string> _chatLines = new List<string>();
        private readonly List<FriendView> _friendViews = new List<FriendView>();

        private Text _chatLog;
        private Text _friendLog;
        private InputField _chatInput;
        private InputField _friendInput;
        private Text _channelLabel;

        private PlayerChat _chat;
        private PlayerFriends _friends;
        private byte _channel = ChatRules.ChannelGlobal;

        private void Awake()
        {
            Instance = this;
            RefreshChatLog();
            RefreshFriendLog();
        }

        private void OnDestroy()
        {
            Unbind();
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Connects the panel's inputs to the local player's social components.</summary>
        public void Bind(PlayerChat chat, PlayerFriends friends)
        {
            Unbind();

            _chat = chat;
            _friends = friends;

            if (_chat != null)
                _chat.RuleFailed += OnChatRuleFailed;

            if (_friends != null)
            {
                _friends.SnapshotChanged += ApplyFriends;
                _friends.EventReceived += OnFriendEvent;
                _friends.RuleFailed += OnFriendRuleFailed;
            }
        }

        private void Unbind()
        {
            if (_chat != null)
                _chat.RuleFailed -= OnChatRuleFailed;

            if (_friends != null)
            {
                _friends.SnapshotChanged -= ApplyFriends;
                _friends.EventReceived -= OnFriendEvent;
                _friends.RuleFailed -= OnFriendRuleFailed;
            }

            _chat = null;
            _friends = null;
        }

        #region Static push (called by the network components)

        /// <summary>Appends an inbound chat envelope (JSON) to the feed.</summary>
        public static void PushChat(string envelopeJson)
        {
            if (Instance != null)
                Instance.AppendChat(envelopeJson);
        }

        /// <summary>Shows a chat toast (rate-limited, backend unavailable, …).</summary>
        public static void PushEvent(int rule)
        {
            if (Instance != null)
                Instance.AppendSystem($"[chat] {((ChatRule)rule).ToString().ToLowerInvariant()}");
        }

        /// <summary>Replaces the friend list from a full snapshot (JSON).</summary>
        public static void PushFriends(string snapshotJson)
        {
            if (Instance != null)
                Instance.ApplyFriends(snapshotJson);
        }

        #endregion

        #region Chat rendering

        private void AppendChat(string envelopeJson)
        {
            SocialBusEnvelope envelope = null;
            try
            {
                envelope = JsonUtility.FromJson<SocialBusEnvelope>(envelopeJson);
            }
            catch (System.Exception)
            {
                // Ignore a malformed frame rather than taking the HUD down.
            }

            if (envelope == null)
                return;

            AppendSystem($"[{envelope.ch}] {envelope.name}: {envelope.body}");
        }

        private void AppendSystem(string line)
        {
            if (string.IsNullOrEmpty(line))
                return;

            _chatLines.Add(line);
            while (_chatLines.Count > MaxChatLines)
                _chatLines.RemoveAt(0);

            RefreshChatLog();
        }

        private void RefreshChatLog()
        {
            if (_chatLog != null)
                _chatLog.text = string.Join("\n", _chatLines);
        }

        private void SetChannel(byte channel)
        {
            _channel = channel;

            if (_channelLabel != null)
                _channelLabel.text = ChatRules.ChannelName(channel);
        }

        private void SendChat()
        {
            if (_chatInput == null)
                return;

            string text = _chatInput.text;
            _chatInput.text = string.Empty;
            _chat?.RequestSend(_channel, text);
        }

        private void OnChatRuleFailed(ChatRule rule)
            => AppendSystem($"[chat] {rule.ToString().ToLowerInvariant()}");

        #endregion

        #region Friend rendering

        private void ApplyFriends(string snapshotJson)
        {
            FriendListSnapshot snapshot = null;
            try
            {
                snapshot = JsonUtility.FromJson<FriendListSnapshot>(snapshotJson);
            }
            catch (System.Exception)
            {
                // Ignore a malformed snapshot.
            }

            _friendViews.Clear();
            if (snapshot?.Friends != null)
                _friendViews.AddRange(snapshot.Friends);

            RefreshFriendLog();
        }

        private void RefreshFriendLog()
        {
            if (_friendLog == null)
                return;

            var lines = new List<string>();
            for (int i = 0; i < _friendViews.Count && lines.Count < MaxFriendLines; i++)
            {
                FriendView view = _friendViews[i];
                string state = view.State switch
                {
                    FriendState.PendingIn => "in",
                    FriendState.PendingOut => "out",
                    FriendState.Accepted => "ok",
                    _ => "?",
                };
                lines.Add($"{(view.Online ? "*" : "-")} {view.PlayerName} [{state}]");
            }

            _friendLog.text = lines.Count == 0 ? "No friends yet." : string.Join("\n", lines);
        }

        private void AddFriend()
        {
            if (_friendInput == null)
                return;

            string name = _friendInput.text;
            _friendInput.text = string.Empty;
            _friends?.RequestAdd(name);
        }

        private void AcceptFirstPending()
        {
            for (int i = 0; i < _friendViews.Count; i++)
            {
                if (_friendViews[i].State == FriendState.PendingIn)
                {
                    _friends?.RequestAccept(_friendViews[i].AccountId);
                    return;
                }
            }

            AppendSystem("[friends] nothing pending");
        }

        private void RemoveFirstFriend()
        {
            for (int i = 0; i < _friendViews.Count; i++)
            {
                if (_friendViews[i].State == FriendState.Accepted)
                {
                    _friends?.RequestRemove(_friendViews[i].AccountId);
                    return;
                }
            }

            AppendSystem("[friends] no accepted friend");
        }

        private void OnFriendEvent(int evt)
            => AppendSystem($"[friends] {((FriendEvent)evt).ToString().ToLowerInvariant()}");

        private void OnFriendRuleFailed(FriendRule rule)
            => AppendSystem($"[friends] {rule.ToString().ToLowerInvariant()}");

        #endregion

        #region Factory

        /// <summary>Builds the chat box and friend panel under <paramref name="parent"/>.</summary>
        public static ChatFeed Create(Transform parent)
        {
            GameObject root = new GameObject(GameObjectName, typeof(RectTransform));
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

        #endregion
    }
}
