using System;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Lightweight, serializable DTO representing player state for saving.
    /// Contains health, mana, and world coordinates.
    /// </summary>
    [Serializable]
    public struct PlayerSaveData
    {
        public int Health;
        public int Mana;
        public Vector3 Coordinates;
    }
}
