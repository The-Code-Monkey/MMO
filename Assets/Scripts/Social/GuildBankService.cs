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
    /// Guild bank deposits, withdrawals, permissions, currency and ledger writes. Every mutation
    /// is one CAS read-modify-write; item/currency moves happen only after the vault write wins.
    /// </summary>
    internal sealed class GuildBankService
    {
        private readonly ISocialRepository _repository;
        private readonly Func<Task<bool>> _ensureConnected;
        private readonly LocalPlayerRegistry _players;
        private readonly Action<GuildBankRecord> _bankChanged;
        private readonly int _casRetries;

        public GuildBankService(
            ISocialRepository repository,
            Func<Task<bool>> ensureConnected,
            LocalPlayerRegistry players,
            Action<GuildBankRecord> bankChanged,
            int casRetries)
        {
            _repository = repository;
            _ensureConnected = ensureConnected;
            _players = players;
            _bankChanged = bankChanged;
            _casRetries = casRetries;
        }

        /// <summary>
        /// Moves the item at the bag cell into the guild bank's tab. One CAS read-modify-write on
        /// the bank object: the bag change happens only after the write wins, so the bag can never
        /// lose an item the bank did not gain.
        /// </summary>
        public async Task<GuildBankResult> DepositAsync(string accountId, int tabIndex, int bagX, int bagY)
        {
            if (!await _ensureConnected())
                return new GuildBankResult(GuildBankRule.BackendUnavailable);

            if (!_players.TryGetGuild(accountId, out PlayerGuild player) || player == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            PlayerInventory bag = player.Inventory;
            if (bag == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            string guildId = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildBankResult result = await RetryBankAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return new GuildBankResult(GuildBankRule.NoGuild);

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                // Resolve through the cell against the live SyncList, never a client-supplied index:
                // the bag may have shifted between the click and this call.
                int itemIndex = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight)
                    .FindAt(bag.Items, bagX, bagY);
                if (itemIndex < 0)
                    return new GuildBankResult(GuildBankRule.InvalidItem);

                ItemInstance item = bag.Items[itemIndex];
                GuildRank rank = GuildRules.RankOf(GuildRules.FindMember(guild, accountId));
                GuildBankRule rule = GuildBankRules.CanDeposit(guild, bank, accountId, tabIndex, item, now);
                if (rule != GuildBankRule.Ok)
                    return new GuildBankResult(rule);

                if (!GuildBankRules.Deposit(bank, tabIndex, item, rank, now))
                    return new GuildBankResult(GuildBankRule.TabFull);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendLedgerAsync(guildId, accountId, GuildBankLedgerAction.Deposit, tabIndex, item, bagX, bagY);
                return new GuildBankResult(GuildBankRule.Ok, item);
            });

            if (result.Rule == GuildBankRule.Ok)
            {
                // The bank is durable. Only now may the bag lose the item, so a failed write can
                // never strand an item outside both containers.
                bag.RemoveItemAt(bagX, bagY);
                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>
        /// Moves the item at the tab cell into the requesting member's bag. Bag space is reserved
        /// before the CAS; if the add still loses a race, the item is CAS-ed back into the bank.
        /// </summary>
        public async Task<GuildBankResult> WithdrawAsync(string accountId, int tabIndex, int gridX, int gridY, int stackCount = -1)
        {
            if (!await _ensureConnected())
                return new GuildBankResult(GuildBankRule.BackendUnavailable);

            if (!_players.TryGetGuild(accountId, out PlayerGuild player) || player == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            PlayerInventory bag = player.Inventory;
            if (bag == null)
                return new GuildBankResult(GuildBankRule.NoGuild);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string guildId = null;
            int cellX = 0;
            int cellY = 0;
            ItemInstance withdrawn = default;

            GuildBankResult result = await RetryBankAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return new GuildBankResult(GuildBankRule.NoGuild);

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                GuildBankRule rule = GuildBankRules.CanWithdraw(guild, bank, accountId, tabIndex, gridX, gridY, now);
                if (rule != GuildBankRule.Ok)
                    return new GuildBankResult(rule);

                // Reserve bag space *before* the CAS: a full bag must refuse without touching the bank.
                GuildBankTab tab = GuildBankRules.FindTab(bank, tabIndex);
                ItemInstance candidate = GuildBankRules.ItemAt(tab, gridX, gridY);
                var bagGrid = new InventoryGrid(PlayerInventory.GridWidth, PlayerInventory.GridHeight);
                if (candidate.IsEmpty ||
                    !bagGrid.TryFindFreeSpace(bag.Items, candidate.Width, candidate.Height, out int freeX, out int freeY))
                    return new GuildBankResult(GuildBankRule.BagFull);

                if (!GuildBankRules.Withdraw(guild, bank, accountId, tabIndex, gridX, gridY, now, out withdrawn))
                    return new GuildBankResult(GuildBankRule.NothingToWithdraw);

                cellX = freeX;
                cellY = freeY;
                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendLedgerAsync(guildId, accountId, GuildBankLedgerAction.Withdraw, tabIndex, withdrawn, gridX, gridY);
                return new GuildBankResult(GuildBankRule.Ok, withdrawn);
            });

            if (result.Rule == GuildBankRule.Ok)
            {
                if (!bag.TryAddExistingItem(withdrawn, cellX, cellY))
                {
                    // The bag shifted between the space check and now (another request landed on the
                    // same player): put the item back so nothing is lost, then tell the player.
                    await ReturnToBankAsync(guildId, tabIndex, withdrawn);
                    return new GuildBankResult(GuildBankRule.BagFull);
                }

                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>Leader-only: replaces a rank's deposit/withdraw permissions on the bank.</summary>
        public async Task<GuildBankRule> SetBankPermissionAsync(
            string accountId, GuildRank rank, bool allowDeposit, bool allowWithdraw, int withdrawLimit,
            long withdrawValueLimit = GuildBankRules.UnlimitedValue, long depositValueLimit = GuildBankRules.UnlimitedValue)
        {
            if (!await _ensureConnected())
                return GuildBankRule.BackendUnavailable;

            if (!GuildBankRules.IsValidPermission(allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit))
                return GuildBankRule.InvalidPermission;

            string guildId = null;
            GuildBankRule result = await RetryBankPermissionAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildBankRule.NoGuild;

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRule rule = GuildBankRules.CanEditPermissions(guild, accountId);
                if (rule != GuildBankRule.Ok)
                    return rule;

                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                GuildBankRules.SetPermission(bank, rank, allowDeposit, allowWithdraw, withdrawLimit, withdrawValueLimit, depositValueLimit);
                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return GuildBankRule.Ok;
            });

            if (result == GuildBankRule.Ok)
                await PushBankAsync(guildId);

            return result;
        }

        /// <summary>
        /// Moves currency from the member's wallet into the guild vault. The vault is written first;
        /// only once that write wins does the wallet lose the currency, so a failed write can never
        /// create currency. The wallet is checked for the funds before the CAS.
        /// </summary>
        public async Task<GuildBankRule> DepositCurrencyAsync(string accountId, string currencyId, long amount)
        {
            if (!await _ensureConnected())
                return GuildBankRule.BackendUnavailable;

            if (!_players.TryGetGuild(accountId, out PlayerGuild player) || player == null)
                return GuildBankRule.NoGuild;

            if (!_players.TryGetWallet(accountId, out PlayerWallet wallet) || wallet == null)
                return GuildBankRule.NoGuild;

            if (string.IsNullOrEmpty(currencyId) || amount <= 0)
                return GuildBankRule.InvalidAmount;

            if (wallet.GetBalance(currencyId) < amount)
                return GuildBankRule.InsufficientFunds;

            string guildId = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildBankRule result = await RetryBankPermissionAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildBankRule.NoGuild;

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                GuildBankRule rule = GuildBankRules.CanDepositCurrency(guild, bank, accountId, currencyId, amount, now);
                if (rule != GuildBankRule.Ok)
                    return rule;

                GuildRank rank = GuildRules.RankOf(GuildRules.FindMember(guild, accountId));
                if (!GuildBankRules.DepositCurrency(bank, currencyId, amount, rank, now))
                    return GuildBankRule.InvalidAmount;

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendCurrencyLedgerAsync(guildId, accountId, GuildBankLedgerAction.Deposit, currencyId, amount);
                return GuildBankRule.Ok;
            });

            if (result == GuildBankRule.Ok)
            {
                // The vault is durable. Only now may the wallet lose the currency; if it somehow
                // cannot, take the amount back out of the vault so no currency is created.
                if (!wallet.Remove(currencyId, amount))
                {
                    await RemoveCurrencyFromBankAsync(guildId, currencyId, amount);
                    return GuildBankRule.Conflict;
                }

                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>
        /// Moves currency from the guild vault into the member's wallet. The vault is debited first;
        /// if the wallet cannot accept it (overflow), the amount is returned to the vault.
        /// </summary>
        public async Task<GuildBankRule> WithdrawCurrencyAsync(string accountId, string currencyId, long amount)
        {
            if (!await _ensureConnected())
                return GuildBankRule.BackendUnavailable;

            if (!_players.TryGetGuild(accountId, out PlayerGuild player) || player == null)
                return GuildBankRule.NoGuild;

            if (!_players.TryGetWallet(accountId, out PlayerWallet wallet) || wallet == null)
                return GuildBankRule.NoGuild;

            if (string.IsNullOrEmpty(currencyId) || amount <= 0)
                return GuildBankRule.InvalidAmount;

            string guildId = null;
            DateTimeOffset now = DateTimeOffset.UtcNow;
            GuildBankRule result = await RetryBankPermissionAsync(async () =>
            {
                SocialIndexRecord index = await _repository.LoadIndexAsync(accountId, CancellationToken.None);
                if (index == null || string.IsNullOrEmpty(index.GuildId))
                    return GuildBankRule.NoGuild;

                guildId = index.GuildId;
                GuildRecord guild = await _repository.LoadGuildAsync(guildId, CancellationToken.None);
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);

                GuildBankRule rule = GuildBankRules.CanWithdrawCurrency(guild, bank, accountId, currencyId, amount, now);
                if (rule != GuildBankRule.Ok)
                    return rule;

                GuildRank rank = GuildRules.RankOf(GuildRules.FindMember(guild, accountId));
                if (!GuildBankRules.WithdrawCurrency(bank, currencyId, amount, rank, now))
                    return GuildBankRule.InsufficientFunds;

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                await AppendCurrencyLedgerAsync(guildId, accountId, GuildBankLedgerAction.Withdraw, currencyId, amount);
                return GuildBankRule.Ok;
            });

            if (result == GuildBankRule.Ok)
            {
                if (!wallet.Add(currencyId, amount))
                {
                    await ReturnCurrencyToBankAsync(guildId, currencyId, amount);
                    return GuildBankRule.Conflict;
                }

                await PushBankAsync(guildId);
            }

            return result;
        }

        /// <summary>
        /// Takes currency back out of the vault when the wallet debit that was supposed to follow a
        /// deposit failed; best effort, and it never spends a rank's quota.
        /// </summary>
        private async Task RemoveCurrencyFromBankAsync(string guildId, string currencyId, long amount)
        {
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return;

            await RetryBankAsync(async () =>
            {
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                if (!GuildBankRules.WithdrawCurrency(bank, currencyId, amount, GuildRank.None, DateTimeOffset.UtcNow))
                    return new GuildBankResult(GuildBankRule.InsufficientFunds);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return new GuildBankResult(GuildBankRule.Ok);
            });

            await PushBankAsync(guildId);
        }

        /// <summary>Puts currency back into the vault after a wallet credit race lost; best effort.</summary>
        private async Task ReturnCurrencyToBankAsync(string guildId, string currencyId, long amount)
        {
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return;

            await RetryBankAsync(async () =>
            {
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                if (!GuildBankRules.DepositCurrency(bank, currencyId, amount, GuildRank.None, DateTimeOffset.UtcNow))
                    return new GuildBankResult(GuildBankRule.InvalidAmount);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return new GuildBankResult(GuildBankRule.Ok);
            });

            await PushBankAsync(guildId);
        }

        /// <summary>
        /// Appends an unsettled territory-tax movement for <paramref name="guildId"/>. Returns false
        /// when the ledger could not be written, so the caller can fail open and leave the seller's
        /// proceeds untouched rather than withholding gold nobody recorded.
        /// </summary>
        public async Task<bool> AppendTerritoryTaxAsync(
            string guildId,
            string sellerAccountId,
            string currencyId,
            long amount,
            long nowTicks)
        {
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return false;

            try
            {
                GuildBankLedgerRecord ledger = await _repository.LoadGuildBankLedgerAsync(
                    guildId, CancellationToken.None)
                    ?? new GuildBankLedgerRecord
                    {
                        GuildId = guildId,
                        Entries = new List<GuildBankLedgerEntry>(),
                    };

                ledger.Entries ??= new List<GuildBankLedgerEntry>();
                ledger.Entries.Add(new GuildBankLedgerEntry
                {
                    EntryId = Guid.NewGuid().ToString("N"),
                    GuildId = guildId,
                    ActorAccountId = sellerAccountId,
                    Action = GuildBankLedgerAction.Tax,
                    TabIndex = -1,
                    ItemName = currencyId,
                    Width = 0,
                    Height = 0,
                    GridX = 0,
                    GridY = 0,
                    StackCount = 0,
                    ValuePerUnit = 1,
                    TotalValue = amount,
                    TimestampTicks = nowTicks,
                    AffectedCells = new List<int>(),
                    Settled = false,
                });

                await _repository.SaveGuildBankLedgerAsync(ledger, CancellationToken.None);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Territory tax ledger append failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Pays every due tax entry into the guild vault. The bank write records each entry id as it
        /// pays, so a retry after a lost ledger write (or a second zone racing this pass) can never
        /// credit the same entry twice. Only when the bank write wins are the ledger rows marked
        /// settled, and that marking is best-effort audit state — the bank ids are the truth.
        /// </summary>
        public async Task SettleTerritoryRevenueAsync(string guildId, long nowTicks)
        {
            if (string.IsNullOrEmpty(guildId))
                return;

            GuildBankLedgerRecord ledger;
            try
            {
                ledger = await _repository.LoadGuildBankLedgerAsync(guildId, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Could not load tax ledger for {guildId}: {e.Message}");
                return;
            }

            if (ledger?.Entries == null)
                return;

            List<GuildBankLedgerEntry> due = TerritoryRevenueRules.DueEntries(ledger.Entries, null);
            if (due.Count == 0)
                return;

            GuildBankResult result = await RetryBankAsync(async () =>
            {
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                bank.SettledTerritoryEntries ??= new List<string>();

                for (int i = 0; i < due.Count; i++)
                {
                    GuildBankLedgerEntry entry = due[i];
                    if (TerritoryRevenueRules.IsSettled(bank, entry))
                        continue;

                    if (!GuildBankRules.DepositCurrency(
                            bank, entry.ItemName, entry.TotalValue, GuildRank.None, DateTimeOffset.UtcNow))
                    {
                        continue;
                    }

                    bank.SettledTerritoryEntries.Add(entry.EntryId);
                    bank.TerritoryRevenue += entry.TotalValue;
                }

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return new GuildBankResult(GuildBankRule.Ok);
            });

            if (result.Rule != GuildBankRule.Ok)
                return;

            for (int i = 0; i < due.Count; i++)
                due[i].Settled = true;

            try
            {
                await _repository.SaveGuildBankLedgerAsync(ledger, CancellationToken.None);
            }
            catch (Exception e)
            {
                // The ids on the bank already prevent a double payment; the flag is cosmetic.
                Debug.LogWarning($"[Social] Could not mark settled tax entries for {guildId}: {e.Message}");
            }

            await PushBankAsync(guildId);
        }

        private async Task AppendCurrencyLedgerAsync(
            string guildId, string actorAccountId, GuildBankLedgerAction action, string currencyId, long amount)
        {
            if (string.IsNullOrEmpty(guildId) || string.IsNullOrEmpty(currencyId) || amount <= 0)
                return;

            try
            {
                GuildBankLedgerRecord ledger = await _repository.LoadGuildBankLedgerAsync(guildId, CancellationToken.None);
                if (ledger == null)
                {
                    ledger = new GuildBankLedgerRecord
                    {
                        GuildId = guildId,
                        Entries = new List<GuildBankLedgerEntry>(),
                    };
                }
                else if (ledger.Entries == null)
                {
                    ledger.Entries = new List<GuildBankLedgerEntry>();
                }

                ledger.Entries.Add(new GuildBankLedgerEntry
                {
                    GuildId = guildId,
                    ActorAccountId = actorAccountId,
                    Action = action,
                    TabIndex = -1,
                    ItemName = currencyId,
                    Width = 0,
                    Height = 0,
                    GridX = 0,
                    GridY = 0,
                    StackCount = 0,
                    ValuePerUnit = 1,
                    TotalValue = amount,
                    TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
                    AffectedCells = new List<int>(),
                });

                await _repository.SaveGuildBankLedgerAsync(ledger, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Guild bank ledger append failed: {e.Message}");
            }
        }

        private async Task<GuildBankRecord> LoadOrCreateBankAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                return null;

            GuildBankRecord bank = await _repository.LoadGuildBankAsync(guildId, CancellationToken.None);
            if (bank == null)
                bank = GuildBankRules.CreateDefault(guildId);
            else
                GuildBankRules.EnsureDefaults(bank);

            return bank;
        }

        /// <summary>Reloads the authoritative bank and pushes it to every local member of the guild.</summary>
        private async Task PushBankAsync(string guildId)
        {
            if (string.IsNullOrEmpty(guildId))
                return;

            GuildBankRecord bank = await _repository.LoadGuildBankAsync(guildId, CancellationToken.None);
            if (bank != null)
                _bankChanged?.Invoke(bank);
        }

        /// <summary>Puts a withdrawn item back into the bank after a bag race lost; best effort.</summary>
        private async Task ReturnToBankAsync(string guildId, int tabIndex, ItemInstance item)
        {
            if (string.IsNullOrEmpty(guildId) || item.IsEmpty)
                return;

            await RetryBankAsync(async () =>
            {
                GuildBankRecord bank = await LoadOrCreateBankAsync(guildId);
                if (!GuildBankRules.Deposit(bank, tabIndex, item))
                    return new GuildBankResult(GuildBankRule.TabFull);

                await _repository.SaveGuildBankAsync(bank, CancellationToken.None);
                return new GuildBankResult(GuildBankRule.Ok);
            });

            await PushBankAsync(guildId);
        }

        private async Task AppendLedgerAsync(string guildId, string actorAccountId, GuildBankLedgerAction action, int tabIndex, ItemInstance item, int gridX, int gridY)
        {
            if (string.IsNullOrEmpty(guildId) || item.IsEmpty)
                return;

            try
            {
                GuildBankLedgerRecord ledger = await _repository.LoadGuildBankLedgerAsync(guildId, CancellationToken.None);
                if (ledger == null)
                {
                    ledger = new GuildBankLedgerRecord
                    {
                        GuildId = guildId,
                        Entries = new List<GuildBankLedgerEntry>(),
                    };
                }
                else if (ledger.Entries == null)
                {
                    ledger.Entries = new List<GuildBankLedgerEntry>();
                }

                ItemDefinition def = ItemDefinitionRegistry.Resolve(item.ItemName);
                long valuePerUnit = def != null ? def.Value : 0;
                int count = item.StackCount < 1 ? 1 : item.StackCount;

                ledger.Entries.Add(new GuildBankLedgerEntry
                {
                    GuildId = guildId,
                    ActorAccountId = actorAccountId,
                    Action = action,
                    TabIndex = tabIndex,
                    ItemName = item.ItemName,
                    Width = item.Width,
                    Height = item.Height,
                    GridX = gridX,
                    GridY = gridY,
                    StackCount = count,
                    ValuePerUnit = valuePerUnit,
                    TotalValue = valuePerUnit * count,
                    TimestampTicks = DateTimeOffset.UtcNow.UtcTicks,
                    AffectedCells = new List<int>(),
                });

                await _repository.SaveGuildBankLedgerAsync(ledger, CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Social] Guild bank ledger append failed: {e.Message}");
            }
        }

        private async Task<GuildBankResult> RetryBankAsync(Func<Task<GuildBankResult>> attempt)
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
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Guild bank mutation failed: {e.Message}");
                    return new GuildBankResult(GuildBankRule.BackendUnavailable);
                }
            }

            return new GuildBankResult(GuildBankRule.Conflict);
        }

        private async Task<GuildBankRule> RetryBankPermissionAsync(Func<Task<GuildBankRule>> attempt)
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
                catch (Exception e)
                {
                    Debug.LogError($"[Social] Guild bank permission edit failed: {e.Message}");
                    return GuildBankRule.BackendUnavailable;
                }
            }

            return GuildBankRule.Conflict;
        }
    }
}
