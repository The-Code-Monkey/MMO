using System;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MMO.Client
{
    /// <summary>
    /// Persists the Input System's binding overrides as JSON under the player's local data.
    ///
    /// A missing or malformed file is never fatal: the action map keeps its code-defined defaults
    /// and the client starts. The path is injectable for tests.
    /// </summary>
    public sealed class InputBindingStore
    {
        public const string FileName = "input-bindings.json";

        private readonly string _path;

        public InputBindingStore(string path = null)
        {
            _path = string.IsNullOrWhiteSpace(path)
                ? Path.Combine(Application.persistentDataPath, FileName)
                : path;
        }

        /// <summary>The file this store reads and writes.</summary>
        public string FilePath => _path;

        /// <summary>Applies saved overrides to <paramref name="asset"/>. Never throws.</summary>
        public void LoadInto(InputActionAsset asset)
        {
            if (asset == null || !File.Exists(_path))
                return;

            try
            {
                LoadJson(asset, File.ReadAllText(_path));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[InputBindingStore] Could not read '{_path}'; using defaults. {exception.Message}");
            }
        }

        /// <summary>Writes the asset's current overrides. A write failure is logged and swallowed.</summary>
        public void Save(InputActionAsset asset)
        {
            if (asset == null)
                return;

            try
            {
                string directory = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                File.WriteAllText(_path, SaveJson(asset));
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[InputBindingStore] Could not write '{_path}'. {exception.Message}");
            }
        }

        /// <summary>Serializes the asset's binding overrides. Exposed for tests and diagnostics.</summary>
        public static string SaveJson(InputActionAsset asset)
            => asset == null ? string.Empty : asset.SaveBindingOverridesAsJson();

        /// <summary>Applies serialized overrides. Malformed JSON is ignored and returns false.</summary>
        public static bool LoadJson(InputActionAsset asset, string json)
        {
            if (asset == null || string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                asset.LoadBindingOverridesFromJson(json);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[InputBindingStore] Invalid binding JSON; using defaults. {exception.Message}");
                return false;
            }
        }
    }
}
