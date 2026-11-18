using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace MMO.Banking
{
    /// <summary>
    /// A world bank teller. Clicking one walks the local player to its stand-off point and opens
    /// the generic bank window when the walk ends — the same interaction a crafting station uses.
    ///
    /// <para>
    /// The prefab is generic: <see cref="Kind"/> decides whether the server tells the client to
    /// open the personal vault or the guild vault. The teller itself owns no storage and no rules;
    /// <see cref="PlayerBank"/> validates the interaction and the matching source in the window
    /// talks to the authoritative bank.
    /// </para>
    /// </summary>
    public sealed class BankTeller : NetworkBehaviour
    {
        /// <summary>How far in front of the teller the player stands to use it.</summary>
        public const float StandOffDistance = 2f;

        /// <summary>How close the player must be for the server to accept the interaction.</summary>
        public const float InteractRange = 3f;

        private readonly SyncVar<int> _kind = new SyncVar<int>();

        /// <summary>Which bank this teller opens.</summary>
        public BankKind Kind => (BankKind)_kind.Value;

        /// <summary>Where the player stands to use this teller: in front, inside the reach.</summary>
        public Vector3 StandOffPosition => transform.position - transform.forward * StandOffDistance;

        [Server]
        public void Configure(BankKind kind) => _kind.Value = (int)kind;

        /// <summary>One reach rule, shared by open/deposit/withdraw and by the client's walk check.</summary>
        public static bool IsWithinReach(Vector3 playerPosition, Vector3 tellerPosition)
            => Vector3.Distance(playerPosition, tellerPosition) <= InteractRange;

        /// <summary>Resolves the local player's bank shell and asks it to open this teller.</summary>
        public void RequestOpenFromLocalPlayer()
        {
            NetworkObject self = NetworkObject;
            if (self == null || self.NetworkManager == null)
                return;

            PlayerBank bank = PlayerBank.FindLocal(self.NetworkManager);
            bank?.RequestOpen(this);
        }
    }
}
