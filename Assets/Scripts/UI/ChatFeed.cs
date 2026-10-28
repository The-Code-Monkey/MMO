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

        public static ChatFeed Instance { get; private set; }

        private readonly ChatLogModel _chatLogModel = new ChatLogModel(MaxChatLines);
        private readonly FriendListModel _friendModel = new FriendListModel();

        internal Text _chatLog;
        internal Text _friendLog;
        internal InputField _chatInput;
        internal InputField _friendInput;
        internal Text _channelLabel;

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

            _chatLogModel.Append(line);
            RefreshChatLog();
        }

        private void RefreshChatLog()
        {
            if (_chatLog != null)
                _chatLog.text = _chatLogModel.Text;
        }

        internal void SetChannel(byte channel)
        {
            _channel = channel;

            if (_channelLabel != null)
                _channelLabel.text = ChatRules.ChannelName(channel);
        }

        internal void SendChat()
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
            _friendModel.Apply(snapshotJson);
            RefreshFriendLog();
        }

        private void RefreshFriendLog()
        {
            if (_friendLog != null)
                _friendLog.text = _friendModel.Text(MaxFriendLines);
        }

        internal void AddFriend()
        {
            if (_friendInput == null)
                return;

            string name = _friendInput.text;
            _friendInput.text = string.Empty;
            _friends?.RequestAdd(name);
        }

        internal void AcceptFirstPending()
        {
            string accountId = _friendModel.FirstPendingAccountId;
            if (accountId == null)
            {
                AppendSystem("[friends] nothing pending");
                return;
            }

            _friends?.RequestAccept(accountId);
        }

        internal void RemoveFirstFriend()
        {
            string accountId = _friendModel.FirstAcceptedAccountId;
            if (accountId == null)
            {
                AppendSystem("[friends] no accepted friend");
                return;
            }

            _friends?.RequestRemove(accountId);
        }

        private void OnFriendEvent(int evt)
            => AppendSystem($"[friends] {((FriendEvent)evt).ToString().ToLowerInvariant()}");

        private void OnFriendRuleFailed(FriendRule rule)
            => AppendSystem($"[friends] {rule.ToString().ToLowerInvariant()}");

        #endregion

        /// <summary>Builds the chat box and friend panel under <paramref name="parent"/>.</summary>
        public static ChatFeed Create(Transform parent) => ChatFeedFactory.Create(parent);
    }
}
