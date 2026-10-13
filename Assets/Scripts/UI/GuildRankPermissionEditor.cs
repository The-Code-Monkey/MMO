using System.Collections.Generic;
using MMO.Social;
using UnityEngine;
using UnityEngine.UI;

namespace MMO.UI
{
    /// <summary>
    /// The three rank rows (Member / Officer / Leader) a bank leader edits: deposit and withdraw
    /// toggles plus a per-period withdraw limit, repainted live from the replicated bank. Saving a
    /// row sends a <c>PlayerGuild.RequestSetPermission</c> intent; whether the asker may edit is a
    /// server question (<see cref="GuildBankRules.CanEditPermissions"/>), so a non-leader's save
    /// simply surfaces as a <see cref="GuildBankRule"/> refusal the player hears about.
    ///
    /// <para>
    /// The control values and the intent they produce are pure (<see cref="Model"/>) so the
    /// toggle-to-limit mapping is EditMode-testable without a MonoBehaviour or a server.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GuildRankPermissionEditor : MonoBehaviour
    {
        /// <summary>Pure mapping between the row controls and the values a permission intent carries.</summary>
        public static class Model
        {
            /// <summary>How the unlimited sentinel reads in the limit box: blank.</summary>
            public const string UnlimitedText = "";

            /// <summary>Builds the limit-box text for a permission's withdraw limit.</summary>
            public static string LimitText(GuildBankPermission permission)
                => permission.WithdrawLimit == GuildBankRules.UnlimitedWithdraw
                    ? UnlimitedText
                    : permission.WithdrawLimit.ToString();

            /// <summary>
            /// Parses the limit box into a withdraw limit. Blank means unlimited; anything below
            /// the unlimited sentinel is nonsense and rejected rather than clamped.
            /// </summary>
            public static bool TryParseLimit(string text, out int limit)
            {
                limit = GuildBankRules.UnlimitedWithdraw;

                if (string.IsNullOrWhiteSpace(text))
                    return true;

                string trimmed = text.Trim();
                if (!int.TryParse(trimmed, out int parsed))
                    return false;

                limit = parsed;
                return parsed >= GuildBankRules.UnlimitedWithdraw;
            }

            /// <summary>Whether the row's current values could be sent (a rank does at least one thing).</summary>
            public static bool IsValid(bool allowDeposit, bool allowWithdraw, int withdrawLimit)
                => GuildBankRules.IsValidPermission(allowDeposit, allowWithdraw, withdrawLimit);
        }

        private sealed class Row
        {
            public GuildRank Rank;
            public Toggle Deposit;
            public Toggle Withdraw;
            public InputField Limit;
        }

        [SerializeField] private RectTransform _rowsContainer;

        private readonly List<Row> _rows = new List<Row>();
        private PlayerGuild _guild;

        /// <summary>Declares where rank rows are parented; public so the factory can hand over a panel.</summary>
        public void SetContainer(RectTransform rowsContainer) => _rowsContainer = rowsContainer;

        /// <summary>
        /// Binds to a guild view: subscribes to its bank changes, builds the three rank rows and
        /// repaints once. Passing null unbinds; a null bank paints the default permissions.
        /// </summary>
        public void Open(PlayerGuild guild)
        {
            Close();

            _guild = guild;
            if (_guild == null)
                return;

            _guild.BankChanged += Repaint;
            BuildRows();
            Repaint();
        }

        /// <summary>Unsubscribes from the guild view. Rows stay on screen as they were.</summary>
        public void Close()
        {
            if (_guild != null)
                _guild.BankChanged -= Repaint;

            _guild = null;
        }

        private void BuildRows()
        {
            if (_rows.Count > 0 || _rowsContainer == null)
                return;

            foreach (GuildRank rank in new[] { GuildRank.Member, GuildRank.Officer, GuildRank.Leader })
                _rows.Add(CreateRow(rank, _rows.Count));
        }

        private Row CreateRow(GuildRank rank, int rowIndex)
        {
            GameObject rowObject = _rowsContainer.gameObject;

            GameObject labelObject = CreateRect(rowObject.transform, $"{rank}Label", 80f, 30f, rowIndex);
            Text label = labelObject.AddComponent<Text>();
            ApplyLabel(label, 13, TextAnchor.MiddleLeft);
            label.text = rank.ToString();

            GameObject depositObject = CreateRect(rowObject.transform, $"{rank}Deposit", 90f, 26f, rowIndex);
            depositObject.transform.localPosition += new Vector3(90f, 0f, 0f);
            Toggle deposit = depositObject.AddComponent<Toggle>();
            deposit.image = depositObject.AddComponent<Image>();
            deposit.targetGraphic = deposit.image;

            GameObject withdrawObject = CreateRect(rowObject.transform, $"{rank}Withdraw", 90f, 26f, rowIndex);
            withdrawObject.transform.localPosition += new Vector3(190f, 0f, 0f);
            Toggle withdraw = withdrawObject.AddComponent<Toggle>();
            withdraw.image = withdrawObject.AddComponent<Image>();
            withdraw.targetGraphic = withdraw.image;

            GameObject limitObject = CreateRect(rowObject.transform, $"{rank}Limit", 130f, 26f, rowIndex);
            limitObject.transform.localPosition += new Vector3(290f, 0f, 0f);
            Image limitBackground = limitObject.AddComponent<Image>();
            limitBackground.color = new Color(1f, 1f, 1f, 0.15f);
            InputField limit = limitObject.AddComponent<InputField>();
            limit.targetGraphic = limitBackground;
            limit.textComponent = CreateText(limitObject.transform, 26f, 26f);
            limit.contentType = InputField.ContentType.IntegerNumber;
            limit.placeholder = CreateText(limitObject.transform, 26f, 26f, "unlimited");

            GameObject saveObject = CreateRect(rowObject.transform, $"{rank}Save", 70f, 26f, rowIndex);
            saveObject.transform.localPosition += new Vector3(430f, 0f, 0f);
            Image saveBackground = saveObject.AddComponent<Image>();
            saveBackground.color = new Color(0.3f, 0.3f, 0.3f, 0.9f);
            Button save = saveObject.AddComponent<Button>();
            save.targetGraphic = saveBackground;
            save.onClick.AddListener(() => Save(rank));
            Text saveLabel = CreateText(saveObject.transform, 70f, 26f);
            ApplyLabel(saveLabel, 13, TextAnchor.MiddleCenter);
            saveLabel.text = "Save";

            return new Row { Rank = rank, Deposit = deposit, Withdraw = withdraw, Limit = limit };
        }

        private void Repaint()
        {
            GuildBankRecord bank = _guild?.Bank;

            for (int i = 0; i < _rows.Count; i++)
            {
                Row row = _rows[i];
                GuildBankPermission permission = GuildBankRules.PermissionFor(bank, row.Rank);
                row.Deposit.isOn = permission.AllowDeposit;
                row.Withdraw.isOn = permission.AllowWithdraw;
                row.Limit.text = Model.LimitText(permission);
            }
        }

        private void Save(GuildRank rank)
        {
            if (_guild == null)
                return;

            Row row = FindRow(rank);
            if (row == null)
                return;

            if (!Model.TryParseLimit(row.Limit.text, out int limit))
                return;

            if (!Model.IsValid(row.Deposit.isOn, row.Withdraw.isOn, limit))
                return;

            _guild.RequestSetPermission((int)rank, row.Deposit.isOn, row.Withdraw.isOn, limit);
        }

        private Row FindRow(GuildRank rank)
        {
            for (int i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].Rank == rank)
                    return _rows[i];
            }

            return null;
        }

        private Text CreateText(Transform parent, float width, float height, string placeholder = null)
        {
            GameObject textObject = new GameObject("Text", typeof(RectTransform));
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Text label = textObject.AddComponent<Text>();
            label.supportRichText = false;
            return label;
        }

        private static GameObject CreateRect(Transform parent, string name, float width, float height, int rowIndex)
        {
            var rectObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)rectObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(0f, -rowIndex * 34f);
            rect.sizeDelta = new Vector2(width, height);
            return rectObject;
        }

        private static void ApplyLabel(Text label, int size, TextAnchor alignment)
        {
            label.font = UIFont.Default;
            label.fontSize = size;
            label.alignment = alignment;
            label.color = Color.white;
            label.raycastTarget = false;
            label.text = string.Empty;
        }

        private void OnDestroy() => Close();
    }
}