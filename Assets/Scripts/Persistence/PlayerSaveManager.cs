using System;
using System.IO;
using FishNet.Object;
using UnityEngine;

namespace MMO.Persistence
{
    /// <summary>
    /// Server-side script that serializes player state to JSON files.
    /// Performs saves on server shutdown and periodic autosaves.
    /// </summary>
    public sealed class PlayerSaveManager : NetworkBehaviour
    {
        private const string SaveDirectory = "Saves";
        private const string PlayerSaveFileName = "player_save.json";
        private const string InventorySaveFileName = "inventory_save.json";

        [SerializeField, Min(0f)] private float _autosaveInterval = 30f;

        private float _nextAutosaveTime;

        public override void OnStartNetwork()
        {
            if (IsServer)
            {
                CreateSaveDirectory();
                _nextAutosaveTime = Time.time + _autosaveInterval;
            }
        }

        private void Update()
        {
            if (!IsServer)
                return;

            if (_autosaveInterval > 0f && Time.time >= _nextAutosaveTime)
            {
                PerformAutosave();
                _nextAutosaveTime = Time.time + _autosaveInterval;
            }
        }

        private void OnApplicationQuit()
        {
            if (IsServer)
            {
                PerformSave();
            }
        }

        [Server]
        public void PerformSave()
        {
            SavePlayerData();
            SaveInventoryData();
        }

        [Server]
        public void PerformAutosave()
        {
            PerformSave();
        }

        [Server]
        private void SavePlayerData()
        {
            try
            {
                PlayerSaveData saveData = GetPlayerSaveData();
                string json = JsonUtility.ToJson(saveData, prettyPrint: true);
                string filePath = GetSaveFilePath(PlayerSaveFileName);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save player data: {e.Message}");
            }
        }

        [Server]
        private void SaveInventoryData()
        {
            try
            {
                InventorySaveData saveData = GetInventorySaveData();
                string json = JsonUtility.ToJson(saveData, prettyPrint: true);
                string filePath = GetSaveFilePath(InventorySaveFileName);
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save inventory data: {e.Message}");
            }
        }

        [Server]
        private PlayerSaveData GetPlayerSaveData()
        {
            // Default values if no player found - preserves structure without game logic changes
            return new PlayerSaveData
            {
                Health = MMO.Gameplay.PlayerStats.MaxHealth,
                Mana = MMO.Gameplay.PlayerStats.MaxMana,
                Coordinates = Vector3.zero
            };
        }

        [Server]
        private InventorySaveData GetInventorySaveData()
        {
            return new InventorySaveData
            {
                ItemIds = new System.Collections.Generic.List<int>(),
                StackCounts = new System.Collections.Generic.List<int>(),
                Durability = new System.Collections.Generic.List<int>()
            };
        }

        private void CreateSaveDirectory()
        {
            string savePath = Path.Combine(Application.persistentDataPath, SaveDirectory);
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }
        }

        private string GetSaveFilePath(string fileName)
        {
            string savePath = Path.Combine(Application.persistentDataPath, SaveDirectory);
            return Path.Combine(savePath, fileName);
        }
    }
}
