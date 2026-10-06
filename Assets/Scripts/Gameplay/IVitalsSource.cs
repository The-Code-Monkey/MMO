using System;

namespace MMO.Gameplay
{
    /// <summary>
    /// The vitals a HUD binds to: current health and mana, the caps they are measured against,
    /// and the one change event that repaints anything showing them.
    ///
    /// <para>
    /// <see cref="PlayerStats"/> is the networked implementation. It raises
    /// <see cref="VitalsChanged"/> from the FishNet <c>SyncVar.OnChange</c> callbacks, so a view
    /// bound to this interface repaints exactly when replicated data changes — never on a frame
    /// timer. The interface exists so a view can be driven from a plain C# fake in a test, with
    /// no network, no <c>NetworkManager</c>, and no spawned player anywhere in sight.
    /// </para>
    /// </summary>
    public interface IVitalsSource
    {
        /// <summary>Current health, as last replicated by the server.</summary>
        int Health { get; }

        /// <summary>Current mana, as last replicated by the server.</summary>
        int Mana { get; }

        /// <summary>The health cap in force: the base maximum plus equipment bonuses.</summary>
        int EffectiveMaxHealth { get; }

        /// <summary>The mana cap in force: the base maximum plus equipment bonuses.</summary>
        int EffectiveMaxMana { get; }

        /// <summary>
        /// Raised whenever any of the four values above changes — health or mana moving, or a
        /// cap shifting because gear was equipped. Subscribers read the new values from the
        /// properties; the event itself carries no payload so one subscription serves every bar.
        /// </summary>
        event Action VitalsChanged;
    }
}
