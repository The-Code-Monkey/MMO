using System;
using System.Collections.Generic;
using MMO.Economy;
using MMO.Inventory;

namespace MMO.UI
{
    /// <summary>
    /// The left pane of the generic bank window: a grid of items plus the bank's stored currency.
    /// The same window binds a <see cref="PersonalBankSource"/> or a <see cref="GuildBankSource"/>;
    /// the view never knows which.
    ///
    /// <para>
    /// <see cref="IInventorySource.RequestEquip"/> is reused as the cell right-click and means
    /// "withdraw this cell"; <see cref="RequestDeposit"/> is the backpack cell right-click and
    /// means "deposit this cell". Both are intents only — the server decides.
    /// </para>
    /// </summary>
    public interface IBankSource : IInventorySource
    {
        /// <summary>Window title, e.g. "Bank" or "Guild Bank".</summary>
        string Title { get; }

        /// <summary>The currency the bank itself holds, for the strip under the grid.</summary>
        IReadOnlyList<CurrencyBalance> Currency { get; }

        /// <summary>Right-click on a backpack cell.</summary>
        void RequestDeposit(int bagX, int bagY);

        /// <summary>Right-click on a bank cell.</summary>
        void RequestWithdraw(int gridX, int gridY);

        /// <summary>Detaches the source's own subscriptions when the window closes.</summary>
        void Unsubscribe();
    }

    /// <summary>
    /// Optional tab strip above the bank grid. A personal bank has none; a guild bank exposes its
    /// tabs. Kept separate so the window stays generic.
    /// </summary>
    public interface IBankTabSource
    {
        IReadOnlyList<string> TabNames { get; }

        int SelectedTab { get; }

        void SelectTab(int index);

        event Action TabsChanged;
    }
}
