using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMO.Auction;
using MMO.Inventory;
using MMO.Persistence;
using UnityEngine;

namespace MMO.Social
{
    /// <summary>
    /// Offline-first mail delivery and claiming. A message is marked claimed under one CAS write
    /// only after the recipient's bag has space, so a failure never destroys an attachment.
    /// </summary>
    internal sealed class MailService
    {
        private readonly Func<ISocialRepository> _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly LocalPlayerRegistry _players;
        private readonly int _casRetries;

        public MailService(
            Func<ISocialRepository> repository,
            Func<Task<bool>> ensureConnected,
            LocalPlayerRegistry players,
            int casRetries)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _players = players;
            _casRetries = casRetries;
        }

        /// <summary>
        /// Finds a message by its exact body. Auction settlement uses it to confirm a hand-off
        /// whose RPC response was lost: the body carries a per-attempt id, so an older delivery
        /// can never be mistaken for the attempt being recovered.
        /// </summary>
        public async Task<string> FindMailIdByBodyAsync(string accountId, string body)
        {
            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(body))
                return null;

            if (!await _ensureConnected())
                return null;

            MailInbox inbox = await _repository().LoadInboxAsync(accountId, CancellationToken.None);
            if (inbox?.Messages == null)
                return null;

            for (int i = 0; i < inbox.Messages.Count; i++)
            {
                if (string.Equals(inbox.Messages[i].Body, body, StringComparison.Ordinal))
                    return inbox.Messages[i].MailId;
            }

            return null;
        }

        /// <summary>
        /// Delivers a message to the recipient's inbox, offline-first: the write lands whether or
        /// not the recipient is currently online, and the login sweep surfaces it later. System
        /// mail (auction delivery) and player-to-player mail share this path.
        /// <para>
        /// A system delivery (an auction item or settlement that cannot be refused) passes
        /// <paramref name="ignoreInboxCap"/> so a full inbox can never destroy an item that has
        /// already been paid for.
        /// </para>
        /// </summary>
        public async Task<MailRule> SendAsync(
            string toAccountId,
            string fromAccountId,
            string subject,
            string body,
            long currency = 0,
            IReadOnlyList<ItemSaveData> attachments = null,
            long expiresAtTicks = 0,
            bool ignoreInboxCap = false,
            CancellationToken ct = default)
        {
            if (!await _ensureConnected())
                return MailRule.BackendUnavailable;

            if (string.IsNullOrEmpty(toAccountId))
                return MailRule.NotFound;

            ISocialRepository repository = _repository();
            return await RetryAsync(async () =>
            {
                MailInbox inbox = await repository.LoadInboxAsync(toAccountId, ct) ?? new MailInbox { AccountId = toAccountId };

                if (!ignoreInboxCap && !MailRules.CanSend(inbox.Messages.Count))
                    return MailRule.InboxFull;

                var message = new MailMessage
                {
                    MailId = Guid.NewGuid().ToString("N"),
                    FromAccountId = fromAccountId,
                    Subject = subject ?? string.Empty,
                    Body = body ?? string.Empty,
                    SentAtTicks = DateTimeOffset.UtcNow.UtcDateTime.Ticks,
                    ExpiresAtTicks = expiresAtTicks,
                    State = MailState.Unread,
                    Currency = currency,
                };

                if (attachments != null)
                {
                    int count = Math.Min(attachments.Count, MailRules.MaxAttachments);
                    for (int i = 0; i < count; i++)
                        message.Attachments.Add(attachments[i]);
                }

                inbox.Messages.Add(message);
                await repository.SaveInboxAsync(inbox, ct);
                return MailRule.Ok;
            }, ct);
        }

        /// <summary>
        /// Claims a local player's message: verifies it is claimable, reserves bag space, marks it
        /// claimed under one CAS write, then grants the attachments and currency to the live player.
        /// A full bag or a lost CAS race leaves the message unclaimed, never a lost item.
        /// </summary>
        public async Task<MailRule> ClaimAsync(string accountId, string mailId, CancellationToken ct = default)
        {
            if (!await _ensureConnected())
                return MailRule.BackendUnavailable;

            if (string.IsNullOrEmpty(accountId) || string.IsNullOrEmpty(mailId))
                return MailRule.NotFound;

            if (!_players.TryGetGuild(accountId, out PlayerGuild player) || player == null)
                return MailRule.NotFound;

            PlayerInventory bag = player.Inventory;
            _players.TryGetWallet(accountId, out PlayerWallet wallet);
            ISocialRepository repository = _repository();

            MailMessage claimed = null;
            MailRule result = await RetryAsync(async () =>
            {
                MailInbox inbox = await repository.LoadInboxAsync(accountId, ct);
                if (inbox == null)
                    return MailRule.NotFound;

                MailMessage message = FindMessage(inbox, mailId);
                if (message == null)
                    return MailRule.NotFound;

                long now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                if (!MailRules.Claimable(message, now))
                    return MailRule.NotClaimable;

                // Reserve space before the write: a bag that cannot hold the items leaves the
                // message untouched, so an item is never destroyed by a failed grant.
                PlayerSaveMapper.ApplyBank(message.Attachments, out List<ItemInstance> items, out _);
                if (!CanFitAll(bag, items))
                    return MailRule.BagFull;

                message.State = MailState.Claimed;
                await repository.SaveInboxAsync(inbox, ct);

                claimed = message;
                return MailRule.Ok;
            }, ct);

            if (result == MailRule.Ok && claimed != null)
            {
                GrantAttachments(bag, claimed.Attachments);
                if (wallet != null && claimed.Currency > 0)
                    wallet.AddGold(claimed.Currency);
            }

            return result;
        }

        /// <summary>
        /// The reconcile-grant path: claims every claimable message for a local player in one CAS
        /// write, then grants the accumulated items and currency. A message whose items do not fit
        /// is left for a later sweep rather than dropped. Returns the number of messages claimed.
        /// </summary>
        public async Task<int> ClaimAllAsync(string accountId, CancellationToken ct = default)
        {
            if (!await _ensureConnected())
                return 0;

            if (string.IsNullOrEmpty(accountId)
                || !_players.TryGetGuild(accountId, out PlayerGuild player)
                || player == null)
                return 0;

            PlayerInventory bag = player.Inventory;
            _players.TryGetWallet(accountId, out PlayerWallet wallet);
            ISocialRepository repository = _repository();

            var grantedAttachments = new List<ItemSaveData>();
            long grantedCurrency = 0;
            int claimedCount = 0;

            MailRule result = await RetryAsync(async () =>
            {
                MailInbox inbox = await repository.LoadInboxAsync(accountId, ct);
                if (inbox == null)
                    return MailRule.NotFound;

                long now = DateTimeOffset.UtcNow.UtcDateTime.Ticks;
                var pending = new List<ItemInstance>();
                var pendingAttachments = new List<ItemSaveData>();
                long pendingCurrency = 0;
                int claimed = 0;

                for (int i = 0; i < inbox.Messages.Count; i++)
                {
                    MailMessage message = inbox.Messages[i];
                    if (!MailRules.Claimable(message, now))
                        continue;

                    PlayerSaveMapper.ApplyBank(message.Attachments, out List<ItemInstance> items, out _);
                    var candidate = new List<ItemInstance>(pending);
                    candidate.AddRange(items);
                    if (!CanFitAll(bag, candidate))
                        continue;

                    pending = candidate;
                    pendingAttachments.AddRange(message.Attachments);
                    pendingCurrency += message.Currency;
                    message.State = MailState.Claimed;
                    claimed++;
                }

                if (claimed == 0)
                    return MailRule.NotFound;

                await repository.SaveInboxAsync(inbox, ct);

                grantedAttachments = pendingAttachments;
                grantedCurrency = pendingCurrency;
                claimedCount = claimed;
                return MailRule.Ok;
            }, ct);

            if (result == MailRule.Ok)
            {
                GrantAttachments(bag, grantedAttachments);
                if (wallet != null && grantedCurrency > 0)
                    wallet.AddGold(grantedCurrency);
            }

            return claimedCount;
        }

        public async Task SweepAsync()
        {
            if (_players.Count == 0)
                return;

            foreach (string accountId in _players.AccountIds())
            {
                try
                {
                    await ClaimAllAsync(accountId, CancellationToken.None);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Mail sweep failed for {accountId}: {e.Message}");
                }
            }
        }

        private static MailMessage FindMessage(MailInbox inbox, string mailId)
        {
            for (int i = 0; i < inbox.Messages.Count; i++)
            {
                if (inbox.Messages[i].MailId == mailId)
                    return inbox.Messages[i];
            }

            return null;
        }

        private static bool CanFitAll(PlayerInventory bag, List<ItemInstance> items)
        {
            if (bag == null || items == null || items.Count == 0)
                return true;

            var copy = new List<ItemInstance>(bag.Items);
            var grid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
            for (int i = 0; i < items.Count; i++)
            {
                ItemInstance item = items[i];
                if (!grid.TryFindFreeSpace(copy, item.Width, item.Height, out int x, out int y))
                    return false;

                item.GridX = x;
                item.GridY = y;
                copy.Add(item);
            }

            return true;
        }

        private static void GrantAttachments(PlayerInventory bag, List<ItemSaveData> attachments)
        {
            if (bag == null || attachments == null)
                return;

            var grid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
            for (int i = 0; i < attachments.Count; i++)
            {
                ItemSaveData entry = attachments[i];
                ItemDefinition definition = ItemDefinitionRegistry.Resolve(entry.ItemName);
                if (definition == null)
                    continue;

                ItemInstance item = ItemInstance.Create(definition, 0, 0, entry.StackCount);
                if (!string.IsNullOrEmpty(entry.InstanceId))
                    item.InstanceId = entry.InstanceId;
                if (entry.Durability > 0 && definition.MaxDurability > 0)
                    item.Durability = Mathf.Min(entry.Durability, definition.MaxDurability);

                if (grid.TryFindFreeSpace(bag.Items, item.Width, item.Height, out int x, out int y))
                {
                    item.GridX = x;
                    item.GridY = y;
                    bag.TryAddExistingItem(item, x, y);
                }
            }
        }

        private async Task<MailRule> RetryAsync(Func<Task<MailRule>> attempt, CancellationToken ct = default)
        {
            for (int i = 0; i < _casRetries; i++)
            {
                try
                {
                    return await attempt();
                }
                catch (StorageVersionConflictException)
                {
                    await SocialBackoff.DelayAsync(i);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Mail mutation failed: {e.Message}");
                    return MailRule.BackendUnavailable;
                }
            }

            return MailRule.Conflict;
        }
    }
}
