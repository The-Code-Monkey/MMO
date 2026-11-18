using System.Collections.Generic;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The mailbox window: a list of messages on the left, the selected message on the right, and
    /// claim / claim-all actions. It is a pure view — every action is a <see cref="PlayerMail"/>
    /// intent and the list is repainted only when the server answers with a fresh inbox.
    ///
    /// <para>
    /// No message is trusted from the client's local copy: the panel renders what the server sent
    /// and enables Claim from <see cref="MailRules.Claimable"/>, but the server re-checks the same
    /// rule before anything moves. A full bag comes back as a rule code, not a lost attachment.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Mail Frame")]
    public sealed class MailFrameView : MonoBehaviour
    {
        public const string GameObjectName = "MailFrame";
        public const float RowHeight = 34f;

        private Text _title;
        private RectTransform _listContent;
        private Text _detailSubject;
        private Text _detailMeta;
        private Text _detailBody;
        private Button _claimButton;
        private Button _claimAllButton;
        private Button _closeButton;

        private readonly List<GameObject> _rows = new();
        private PlayerMail _mail;
        private MailInbox _inbox;
        private string _selectedId;

        /// <summary>Default key that opens/closes the mailbox from the HUD.</summary>
        public KeyCode OpenKey { get; set; } = KeyCode.M;

        public bool IsOpen => gameObject.activeSelf;

        public PlayerMail Mail => _mail;

        public MailInbox Inbox => _inbox;

        public string SelectedMailId => _selectedId;

        /// <summary>Declares the widgets a factory built; public so tests can inject fakes.</summary>
        public void SetHosts(
            Text title,
            RectTransform listContent,
            Text detailSubject,
            Text detailMeta,
            Text detailBody,
            Button claimButton,
            Button claimAllButton,
            Button closeButton)
        {
            _title = title;
            _listContent = listContent;
            _detailSubject = detailSubject;
            _detailMeta = detailMeta;
            _detailBody = detailBody;
            _claimButton = claimButton;
            _claimAllButton = claimAllButton;
            _closeButton = closeButton;

            if (_claimButton != null)
                _claimButton.onClick.AddListener(HandleClaim);
            if (_claimAllButton != null)
                _claimAllButton.onClick.AddListener(HandleClaimAll);
            if (_closeButton != null)
                _closeButton.onClick.AddListener(Conceal);
        }

        /// <summary>Binds this view to one player's mailbox; unsubscribes any previous player.</summary>
        public void Bind(PlayerMail mail)
        {
            Unbind();
            _mail = mail;
            if (_mail == null)
                return;

            _mail.InboxReceived += SetInbox;
            _mail.Sent += OnSent;
        }

        /// <summary>Stops listening; the built rows stay on screen until the next bind repaints.</summary>
        public void Unbind()
        {
            if (_mail != null)
            {
                _mail.InboxReceived -= SetInbox;
                _mail.Sent -= OnSent;
            }

            _mail = null;
        }

        /// <summary>Shows the window and asks the server for the current inbox.</summary>
        public void Reveal()
        {
            gameObject.SetActive(true);
            _mail?.RequestInbox();
        }

        /// <summary>Hides the window without unbinding it.</summary>
        public void Conceal() => gameObject.SetActive(false);

        public void Toggle()
        {
            if (IsOpen)
                Conceal();
            else
                Reveal();
        }

        /// <summary>Replaces the list from a server snapshot and keeps the selection when possible.</summary>
        public void SetInbox(MailInbox inbox)
        {
            _inbox = inbox;
            if (string.IsNullOrEmpty(_selectedId) || Find(_selectedId) == null)
                _selectedId = inbox?.Messages != null && inbox.Messages.Count > 0
                    ? inbox.Messages[0]?.MailId
                    : null;

            RepaintRows();
            RepaintDetail();
        }

        /// <summary>Selects a message locally; nothing is sent to the server.</summary>
        public void SelectMail(string mailId)
        {
            _selectedId = mailId;
            RepaintDetail();
        }

        /// <summary>Claim action; the server re-validates and answers with a fresh inbox.</summary>
        public void HandleClaim()
        {
            if (_mail != null && !string.IsNullOrEmpty(_selectedId))
                _mail.RequestClaim(_selectedId);
        }

        /// <summary>Claims every claimable message.</summary>
        public void HandleClaimAll()
        {
            _mail?.RequestClaimAll();
        }

        private void OnSent() => RepaintRows();

        private void RepaintRows()
        {
            ClearRows();

            if (_title != null)
                _title.text = MailUiFormat.Title(MailUiFormat.UnreadCount(_inbox));

            if (_listContent == null || _inbox?.Messages == null)
                return;

            for (int i = 0; i < _inbox.Messages.Count; i++)
            {
                MailMessage message = _inbox.Messages[i];
                if (message == null)
                    continue;

                _rows.Add(CreateRow(message, _rows.Count));
            }
        }

        private GameObject CreateRow(MailMessage message, int index)
        {
            var rowObject = new GameObject($"Mail {index}", typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)rowObject.transform;
            rect.SetParent(_listContent, false);
            rect.sizeDelta = new Vector2(0f, RowHeight);

            LayoutElement layout = rowObject.AddComponent<LayoutElement>();
            layout.preferredHeight = RowHeight;

            Image background = rowObject.GetComponent<Image>();
            background.color = message.MailId == _selectedId
                ? new Color(0.55f, 0.45f, 0.2f, 0.55f)
                : new Color(1f, 1f, 1f, 0.04f);

            Button button = rowObject.AddComponent<Button>();
            button.targetGraphic = background;
            string mailId = message.MailId;
            button.onClick.AddListener(() => SelectMail(mailId));

            var labelObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = (RectTransform)labelObject.transform;
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8f, 0f);
            labelRect.offsetMax = new Vector2(-8f, 0f);

            Text label = labelObject.GetComponent<Text>();
            label.font = UIFont.Default;
            label.fontSize = 13;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = message.State == MailState.Unread ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.text = message.Subject;

            return rowObject;
        }

        private void RepaintDetail()
        {
            MailMessage message = Find(_selectedId);

            if (_detailSubject != null)
                _detailSubject.text = message != null ? message.Subject : "No message selected";

            if (_detailMeta != null)
                _detailMeta.text = message != null
                    ? MailUiFormat.Header(message) + "\n" + MailUiFormat.Contents(message)
                    : string.Empty;

            if (_detailBody != null)
                _detailBody.text = message != null ? message.Body : string.Empty;

            long nowTicks = System.DateTime.UtcNow.Ticks;

            if (_claimButton != null)
                _claimButton.interactable = message != null && MailRules.Claimable(message, nowTicks);

            if (_claimAllButton != null)
                _claimAllButton.interactable = MailUiFormat.ClaimableCount(_inbox, nowTicks) > 0;

            // Repaint the selected-row tint without rebuilding the list.
            for (int i = 0; i < _rows.Count && _inbox?.Messages != null && i < _inbox.Messages.Count; i++)
            {
                Image background = _rows[i].GetComponent<Image>();
                if (background != null)
                {
                    MailMessage row = _inbox.Messages[i];
                    background.color = row != null && row.MailId == _selectedId
                        ? new Color(0.55f, 0.45f, 0.2f, 0.55f)
                        : new Color(1f, 1f, 1f, 0.04f);
                }
            }
        }

        private MailMessage Find(string mailId)
        {
            if (string.IsNullOrEmpty(mailId) || _inbox?.Messages == null)
                return null;

            for (int i = 0; i < _inbox.Messages.Count; i++)
            {
                if (_inbox.Messages[i] != null && _inbox.Messages[i].MailId == mailId)
                    return _inbox.Messages[i];
            }

            return null;
        }

        private void ClearRows()
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i] != null)
                    ViewObject.Destroy(_rows[i]);
            }

            _rows.Clear();
        }

        private void OnDestroy() => Unbind();
    }
}
