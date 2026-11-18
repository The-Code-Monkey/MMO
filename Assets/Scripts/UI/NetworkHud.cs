using MMO.Content;
using MMO.Gameplay;
using MMO.Inventory;
using UnityEngine;

namespace MMO.UI
{
    /// <summary>
    /// The root of the client HUD: the four screens built by
    /// <see cref="NetworkHudFactory"/> and the bind points gameplay code drives.
    ///
    /// <para>
    /// Binding is deliberately explicit and one-way. Whoever owns the local player's spawned
    /// components calls <see cref="BindPlayer"/> once they exist; whoever selects a target calls
    /// <see cref="BindTarget"/>. From then on every repaint on screen is a response to a
    /// FishNet change callback — this component runs no <c>Update</c>, and neither does anything
    /// it holds.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("MMO/UI/Network HUD")]
    public sealed class NetworkHud : MonoBehaviour
    {
        private PlayerHudView _playerHud;
        private TargetFrameView _targetFrame;
        private InventoryGridView _inventoryGrid;
        private ActionBarView _actionBar;
        private ChatFeed _chatFeed;
        private BankFrameView _bankFrame;
        private TradeFrameView _tradeFrame;
        private CraftingPanelView _craftingPanel;
        private MailFrameView _mailFrame;
        private CollapsiblePanelView _inventoryWindow;
        private CollapsiblePanelView _equipmentWindow;
        private EquipmentPanelView _equipmentPanel;
        private MountCastBarView _mountCastBar;
        private DialogueView _dialogueView;
        private ZoneTransferPromptView _zoneTransferPrompt;

        /// <summary>The health/mana pair, or null before views were assigned.</summary>
        public PlayerHudView PlayerHud => _playerHud;

        /// <summary>The target frame, or null before views were assigned.</summary>
        public TargetFrameView TargetFrame => _targetFrame;

        /// <summary>The bag grid, or null before views were assigned.</summary>
        public InventoryGridView InventoryGrid => _inventoryGrid;

        /// <summary>The action bar, or null before views were assigned.</summary>
        public ActionBarView ActionBar => _actionBar;

        /// <summary>The chat box and friend list, or null when the factory was told to omit it.</summary>
        public ChatFeed ChatFeed => _chatFeed;

        /// <summary>The generic bank window, or null when the factory was told to omit it.</summary>
        public BankFrameView BankFrame => _bankFrame;

        /// <summary>The direct-trade window, or null when the factory was told to omit it.</summary>
        public TradeFrameView TradeFrame => _tradeFrame;

        /// <summary>The mailbox window, or null before views were assigned.</summary>
        public MailFrameView MailFrame => _mailFrame;

        /// <summary>The one unified crafting window, or null before views were assigned.</summary>
        public CraftingPanelView CraftingPanel => _craftingPanel;

        /// <summary>The bag window's collapse controller, or null before views were assigned.</summary>
        public CollapsiblePanelView InventoryWindow => _inventoryWindow;

        /// <summary>The equipment window's collapse controller, or null before views were assigned.</summary>
        public CollapsiblePanelView EquipmentWindow => _equipmentWindow;

        /// <summary>The worn-gear panel, or null before views were assigned.</summary>
        public EquipmentPanelView EquipmentPanel => _equipmentPanel;

        /// <summary>The centre-screen mount cast bar, or null before views were assigned.</summary>
        public MountCastBarView MountCastBar => _mountCastBar;

        /// <summary>The dialogue panel, or null before views were assigned.</summary>
        public DialogueView DialogueView => _dialogueView;

        /// <summary>The zone-gate prompt, or null before views were assigned.</summary>
        public ZoneTransferPromptView ZoneTransferPrompt => _zoneTransferPrompt;

        /// <summary>
        /// Hands the assembled screens to the HUD; called by the factory.
        /// </summary>
        public void SetViews(
            PlayerHudView playerHud,
            TargetFrameView targetFrame,
            InventoryGridView inventoryGrid,
            ActionBarView actionBar,
            ChatFeed chatFeed = null,
            CollapsiblePanelView inventoryWindow = null,
            CollapsiblePanelView equipmentWindow = null,
            EquipmentPanelView equipmentPanel = null,
            MountCastBarView mountCastBar = null,
            ZoneTransferPromptView zoneTransferPrompt = null)
        {
            _playerHud = playerHud;
            _targetFrame = targetFrame;
            _inventoryGrid = inventoryGrid;
            _actionBar = actionBar;
            _chatFeed = chatFeed;
            _inventoryWindow = inventoryWindow;
            _equipmentWindow = equipmentWindow;
            _equipmentPanel = equipmentPanel;
            _mountCastBar = mountCastBar;
            _zoneTransferPrompt = zoneTransferPrompt;
        }

        /// <summary>Gives the HUD the generic bank window the factory built; starts concealed.</summary>
        public void SetBankFrame(BankFrameView bankFrame)
        {
            _bankFrame = bankFrame;
            _bankFrame?.Conceal();
        }

        /// <summary>Gives the HUD the direct-trade window the factory built; starts concealed.</summary>
        public void SetTradeFrame(TradeFrameView tradeFrame)
        {
            _tradeFrame = tradeFrame;
            _tradeFrame?.Conceal();
        }

        /// <summary>Gives the HUD the crafting window the factory built; starts concealed.</summary>
        public void SetCraftingPanel(CraftingPanelView craftingPanel)
        {
            _craftingPanel = craftingPanel;
            _craftingPanel?.Conceal();
        }

        /// <summary>Gives the HUD the mailbox window the factory built; starts concealed.</summary>
        public void SetMailFrame(MailFrameView mailFrame)
        {
            _mailFrame = mailFrame;
            _mailFrame?.Conceal();
        }

        /// <summary>Gives the HUD the dialogue panel the factory built; starts concealed.</summary>
        public void SetDialogueView(DialogueView dialogueView)
        {
            _dialogueView = dialogueView;
            _dialogueView?.Hide();
        }

        /// <summary>
        /// Connects the chat box to the local player's social components. Both may be null — the
        /// panel simply stays quiet until a later call binds it.
        /// </summary>
        public void BindChat(Social.PlayerChat chat, Social.PlayerFriends friends)
        {
            if (_chatFeed != null)
                _chatFeed.Bind(chat, friends);
        }

        /// <summary>
        /// Binds the player's screens to their networked sources. Any argument may be null — a
        /// screen bound to nothing simply stays quiet until a later call binds it, so a HUD can
        /// exist before the object it describes has spawned.
        /// </summary>
        public void BindPlayer(
            IVitalsSource vitals,
            IInventorySource inventory = null,
            IAbilityBarSource abilities = null,
            ICraftingSource crafting = null,
            IMountSource mount = null)
        {
            if (_playerHud != null)
                _playerHud.Bind(vitals);

            if (_inventoryGrid != null)
                _inventoryGrid.Bind(inventory);

            if (_equipmentPanel != null)
                _equipmentPanel.Bind(inventory as IEquipmentSource);

            if (_mountCastBar != null)
                _mountCastBar.Bind(mount);

            if (_actionBar != null)
                _actionBar.Bind(abilities);

            if (_craftingPanel != null)
                _craftingPanel.Bind(crafting);
        }

        /// <summary>Binds the dialogue panel to the local player's dialogue shell.</summary>
        public void BindDialogue(PlayerDialogue dialogue)
        {
            if (_dialogueView != null)
                _dialogueView.Bind(dialogue);
        }

        /// <summary>Shows <paramref name="target"/> in the frame; null hides the frame.</summary>
        public void BindTarget(ITargetSource target)
        {
            if (_targetFrame != null)
                _targetFrame.Bind(target);
        }

        /// <summary>
        /// Opens the generic bank window on whichever source the teller selected. A guild teller
        /// also passes its <see cref="Social.PlayerGuild"/> so the rank editor appears.
        /// </summary>
        public void OpenBank(IBankSource source, PlayerInventory bag, Social.PlayerGuild guild = null)
            => _bankFrame?.Reveal(source, bag, guild);

        /// <summary>Closes the bank window without touching its bindings.</summary>
        public void CloseBank() => _bankFrame?.Conceal();

        /// <summary>Binds the trade window to the local player's trade and bag.</summary>
        public void BindTrade(Social.PlayerTrade trade, PlayerInventory bag)
            => _tradeFrame?.Reveal(trade, bag);

        /// <summary>Detaches every screen from every source. The HUD stops repainting entirely.</summary>
        public void Unbind()
        {
            if (_playerHud != null)
                _playerHud.Unbind();

            if (_targetFrame != null)
                _targetFrame.Unbind();

            if (_inventoryGrid != null)
                _inventoryGrid.Unbind();

            if (_equipmentPanel != null)
                _equipmentPanel.Unbind();

            if (_mountCastBar != null)
                _mountCastBar.Unbind();

            if (_actionBar != null)
                _actionBar.Unbind();

            if (_craftingPanel != null)
            {
                _craftingPanel.Unbind();
                _craftingPanel.Conceal();
            }

            if (_dialogueView != null)
            {
                _dialogueView.Unbind();
                _dialogueView.Hide();
            }

            if (_bankFrame != null)
                _bankFrame.Conceal();

            if (_tradeFrame != null)
                _tradeFrame.Conceal();

            _zoneTransferPrompt?.Hide();
        }

        private void OnDestroy() => Unbind();
    }
}
